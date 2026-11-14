using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Facts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dishhive.Api.Tests.Services;

public sealed class RecipeFactsAssessmentServiceTests : IAsyncDisposable
{
    [Fact]
    public void RepeatedAssessmentsWithIdenticalClassesRetainTheirOwnTimesWithoutPendingDuplicates()
    {
        var first = DateTime.UtcNow.AddDays(-1);
        var recipe = new Recipe { Title = "Repeated assessment", DietaryFactsStatus = DietaryFactsStatus.AiDetected,
            DietaryFactsAssessedAt = first, DietaryFactsAssessedRevision = 1 };
        recipe.DietaryFacts.Add(new() { IngredientClass = IngredientClass.Milk });
        RecipeFactsStateService.Archive(recipe);
        recipe.DietaryFactsAssessedAt = first.AddHours(1);
        RecipeFactsStateService.Archive(recipe);
        recipe.DietaryFactsStatus = DietaryFactsStatus.Pending;
        RecipeFactsStateService.Archive(recipe);
        Assert.Equal(2, recipe.DietaryFactsHistory.Count);
        Assert.All(recipe.DietaryFactsHistory, history => Assert.Equal(DietaryFactsStatus.AiDetected, history.Status));
        Assert.Equal(2, recipe.DietaryFactsHistory.Select(history => history.AssessedAt).Distinct().Count());
    }

    private sealed class ControlledExtractor : IRecipeFactsExtractor
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsAvailable => true;
        public Task Entered => _entered.Task;
        public void Release() => _release.TrySetResult();

        public async Task<IReadOnlyList<IngredientClass>?> ExtractAsync(
            string title,
            IReadOnlyList<string> ingredients,
            CancellationToken cancellationToken = default,
            string? operationId = null)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return [IngredientClass.Milk];
        }
    }

    private readonly ServiceProvider _provider;
    private readonly ControlledExtractor _extractor = new();
    private readonly RecipeFactsAssessmentService _worker;

    public RecipeFactsAssessmentServiceTests()
    {
        var services = new ServiceCollection();
        var databaseName = $"facts-worker-{Guid.NewGuid():N}";
        services.AddDbContext<DishhiveDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        _provider = services.BuildServiceProvider();
        _worker = new RecipeFactsAssessmentService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _extractor,
            NullLogger<RecipeFactsAssessmentService>.Instance);
    }

    [Fact]
    public async Task ReassessmentRetainsThePreviousAiAssessmentAndNeverQueuesHumanConfirmation()
    {
        var recipe = await AddRecipeAsync();
        var assessedAt = DateTime.UtcNow.AddDays(-1);
        await using (var scope = _provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            var current = await context.Recipes.SingleAsync(item => item.Id == recipe.Id);
            current.DietaryFactsStatus = DietaryFactsStatus.AiDetected;
            current.DietaryFactsAssessedAt = assessedAt;
            current.DietaryFactsAssessedRevision = current.Revision;
            current.DietaryFacts.Add(new() { IngredientClass = IngredientClass.Eggs });
            context.Recipes.Add(new() { Title = "Human confirmation", DietaryFactsStatus = DietaryFactsStatus.UserConfirmed,
                DietaryFactsAssessedAt = assessedAt, FactsConfirmationVersion = 2 });
            await context.SaveChangesAsync();
        }
        Assert.True(await _worker.EnqueueAsync(recipe.Id));
        var processing = _worker.ProcessNextAsync();
        await _extractor.Entered;
        _extractor.Release();
        Assert.True(await processing);
        await using var readScope = _provider.CreateAsyncScope();
        var db = readScope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var updated = await db.Recipes.Include(item => item.DietaryFacts).Include(item => item.DietaryFactsHistory)
            .SingleAsync(item => item.Id == recipe.Id);
        Assert.Equal(DietaryFactsStatus.AiDetected, updated.DietaryFactsStatus);
        Assert.Equal(IngredientClass.Milk, Assert.Single(updated.DietaryFacts).IngredientClass);
        var history = Assert.Single(updated.DietaryFactsHistory);
        Assert.Equal(DietaryFactsStatus.AiDetected, history.Status);
        Assert.Equal("Eggs", history.Contains);
        var confirmed = await db.Recipes.SingleAsync(item => item.Title == "Human confirmation");
        Assert.False(await _worker.EnqueueAsync(confirmed.Id));
        Assert.Equal(2, confirmed.FactsConfirmationVersion);
    }

    [Fact]
    public async Task ResultForOldRevision_CannotOverwriteNewHumanConfirmation()
    {
        var recipe = await AddRecipeAsync();
        (await _worker.EnqueueAsync(recipe.Id)).Should().BeTrue();
        var processing = _worker.ProcessNextAsync();
        await _extractor.Entered;

        await using (var scope = _provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            var current = await context.Recipes.SingleAsync(item => item.Id == recipe.Id);
            current.Revision++;
            current.FactsConfirmationVersion++;
            current.DietaryFactsStatus = DietaryFactsStatus.UserConfirmed;
            await context.SaveChangesAsync();
        }

        _extractor.Release();
        await processing;

        await using var verificationScope = _provider.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        (await verification.Recipes.SingleAsync(item => item.Id == recipe.Id))
            .DietaryFactsStatus.Should().Be(DietaryFactsStatus.UserConfirmed);
        (await verification.RecipeEnrichmentJobs.SingleAsync()).State
            .Should().Be(RecipeEnrichmentJobState.Superseded);
    }

    [Fact]
    public async Task Enqueue_CoalescesPendingWorkToLatestRevision()
    {
        var recipe = await AddRecipeAsync();
        await _worker.EnqueueAsync(recipe.Id);
        await using (var scope = _provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            var current = await context.Recipes.SingleAsync(item => item.Id == recipe.Id);
            current.Revision = 7;
            await context.SaveChangesAsync();
        }

        await _worker.EnqueueAsync(recipe.Id);

        await using var verificationScope = _provider.CreateAsyncScope();
        var jobs = await verificationScope.ServiceProvider.GetRequiredService<DishhiveDbContext>()
            .RecipeEnrichmentJobs.ToListAsync();
        jobs.Should().ContainSingle().Which.RecipeRevision.Should().Be(7);
    }

    [Fact]
    public async Task RestartRecovery_RequeuesExpiredLease()
    {
        var recipe = await AddRecipeAsync();
        await using (var scope = _provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            context.RecipeEnrichmentJobs.Add(new RecipeEnrichmentJob
            {
                RecipeId = recipe.Id,
                RecipeRevision = recipe.Revision,
                OperationId = "restart-test",
                State = RecipeEnrichmentJobState.Running,
                LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1)
            });
            await context.SaveChangesAsync();
        }

        await _worker.RecoverExpiredLeasesAsync();

        await using var verificationScope = _provider.CreateAsyncScope();
        var job = await verificationScope.ServiceProvider.GetRequiredService<DishhiveDbContext>()
            .RecipeEnrichmentJobs.SingleAsync();
        job.State.Should().Be(RecipeEnrichmentJobState.Pending);
        job.LeaseExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Cancellation_ReleasesLeaseForAFutureWorker()
    {
        var recipe = await AddRecipeAsync();
        await _worker.EnqueueAsync(recipe.Id);
        using var cancellation = new CancellationTokenSource();
        var processing = _worker.ProcessNextAsync(cancellation.Token);
        await _extractor.Entered;

        cancellation.Cancel();

        await FluentActions.Awaiting(() => processing).Should().ThrowAsync<OperationCanceledException>();
        await using var verificationScope = _provider.CreateAsyncScope();
        var job = await verificationScope.ServiceProvider.GetRequiredService<DishhiveDbContext>()
            .RecipeEnrichmentJobs.SingleAsync();
        job.State.Should().Be(RecipeEnrichmentJobState.Pending);
        job.LeaseExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task RecipeWithoutIngredients_DoesNotRemainPendingAfterWorkerRejectsIt()
    {
        Guid recipeId;
        await using (var scope = _provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            var recipe = new Recipe { Title = "No ingredients yet" };
            context.Recipes.Add(recipe);
            await context.SaveChangesAsync();
            recipeId = recipe.Id;
        }
        await _worker.EnqueueAsync(recipeId);
        await _worker.ProcessNextAsync();
        await using var verificationScope = _provider.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        (await verification.Recipes.SingleAsync()).DietaryFactsStatus.Should().Be(DietaryFactsStatus.Failed);
        (await verification.RecipeEnrichmentJobs.SingleAsync()).State.Should().Be(RecipeEnrichmentJobState.Failed);
    }

    private async Task<Recipe> AddRecipeAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var recipe = new Recipe
        {
            Id = Guid.NewGuid(),
            Title = "Worker race",
            Ingredients = [new RecipeIngredient { Name = "cream", SortOrder = 0 }]
        };
        context.Recipes.Add(recipe);
        await context.SaveChangesAsync();
        return recipe;
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
}

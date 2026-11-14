using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Localization;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;

namespace Dishhive.Api.Tests.Integration;

public sealed class PostgreSqlAcceptanceTests : IAsyncLifetime
{
    [PostgresFact]
    public async Task NewRecipeAndEnrichmentJobs_CommitTogetherWithGeneratedRecipeIds()
    {
        await using var scope = _provider!.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var extractor = Substitute.For<Dishhive.Api.Services.Facts.IRecipeFactsExtractor>();
        extractor.IsAvailable.Returns(true);
        using var worker = new Dishhive.Api.Services.Facts.RecipeFactsAssessmentService(
            _provider!.GetRequiredService<IServiceScopeFactory>(), extractor,
            NullLogger<Dishhive.Api.Services.Facts.RecipeFactsAssessmentService>.Instance);
        var first = new Recipe { Title = "New first import" };
        var second = new Recipe { Title = "New second import" };
        context.Recipes.AddRange(first, second);
        await worker.StageAsync(context, first);
        await worker.StageAsync(context, second);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var jobs = await context.RecipeEnrichmentJobs.Include(j => j.Recipe).ToListAsync();
        Assert.Equal(2, jobs.Count);
        Assert.Equal(2, jobs.Select(j => j.RecipeId).Distinct().Count());
        Assert.All(jobs, j => Assert.NotEqual(Guid.Empty, j.RecipeId));
        Assert.Equal(new[] { "New first import", "New second import" }, jobs.Select(j => j.Recipe!.Title).Order().ToArray());
    }

    [PostgresFact]
    public async Task Migration_LegacyReservationsAndUnknownInputVersionsSurviveUpgrade()
    {
        await using var scope = _provider!.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261112215618_RecordAiOperationAttempts");
        var id = Guid.NewGuid();
        var day = DateOnly.FromDateTime(DateTime.Today).AddDays(2);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PlannedMeals" ("Id", "Date", "MealType", "Course", "DishName", "FreezyItemRef", "FreezyItemQuantity")
            VALUES ({id}, {day}, 2, 0, 'Legacy dinner', 'legacy-stock', 2)
            """);
        await migrator.MigrateAsync();
        var meal = await context.PlannedMeals.SingleAsync(meal => meal.Id == id);
        Assert.Equal("Legacy dinner", meal.DishName);
        Assert.Equal(2, meal.FreezyItemQuantity);
        Assert.Null(meal.FreezyItemFingerprint);
        context.AiReviewEvents.Add(new() { RecipeId = Guid.NewGuid(), Kind = "factsConfirmed", Task = "recipe.facts" });
        context.AiTaskRuns.Add(new() { Task = "recipe.facts", StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();
        Assert.Null((await context.AiTaskRuns.SingleAsync()).InputRevisionsJson);
        Assert.Single(await context.AiReviewEvents.ToListAsync());
    }
    private ServiceProvider? _provider;
    private string? _connectionString;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("DISHHIVE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var connection = new NpgsqlConnectionStringBuilder(configured)
        {
            Database = $"dishhive_test_{Guid.NewGuid():N}"
        };
        _connectionString = connection.ConnectionString;
        var services = new ServiceCollection();
        services.AddSingleton<Dishhive.Api.Services.Time.IHouseholdClock>(TestHouseholdClock.System);
        services.AddDbContext<DishhiveDbContext>(options => options.UseNpgsql(_connectionString));
        var freezy = Substitute.For<IFreezyClient>();
        freezy.GetFrozenItemsAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<FrozenItem>>([
                new() { Id = "last-lasagne", Name = "Lasagne", Quantity = 1 }
            ]));
        services.AddSingleton(freezy);
        services.AddScoped<FreezerAvailabilityService>();
        services.AddScoped<AttendancePolicy>();
        services.AddScoped<UserMessageLocalizer>();
        services.AddScoped(_ => Substitute.For<IRecipeImportService>());
        services.AddScoped<MealSuggestionAcceptanceService>(provider => new(
            provider.GetRequiredService<DishhiveDbContext>(),
            provider.GetRequiredService<IRecipeImportService>(),
            provider.GetRequiredService<FreezerAvailabilityService>(),
            provider.GetRequiredService<AttendancePolicy>(),
            provider.GetRequiredService<UserMessageLocalizer>(),
            NullLogger<MealSuggestionAcceptanceService>.Instance));
        _provider = services.BuildServiceProvider();
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DishhiveDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is null) return;
        await using (var scope = _provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DishhiveDbContext>().Database.EnsureDeletedAsync();
        await _provider.DisposeAsync();
    }

    [PostgresFact]
    public async Task Migrations_CreateDurableCorrectnessTables()
    {
        await using var scope = _provider!.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        (await context.Database.CanConnectAsync()).Should().BeTrue();
        await context.RecipeEnrichmentJobs.CountAsync();
        await context.MealSuggestionBatches.CountAsync();
        await context.RecipeDietaryFactHistory.CountAsync();
    }

    [PostgresFact]
    public async Task Migration_ExistingTaskRowsKeepUnknownProviderAttemptAndNewOrdinalsPersist()
    {
        await using var scope = _provider!.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var legacy = new AiTaskRun { Task = "recipe.facts", StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow };
        context.AiTaskRuns.Add(legacy);
        await context.SaveChangesAsync();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260913184421_CoalescePendingEnrichment");
        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();
        (await context.AiTaskRuns.SingleAsync()).OperationAttempt.Should().BeNull();
        context.AiTaskRuns.Add(new AiTaskRun { Task = "recipe.facts", StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow, OperationAttempt = 2 });
        await context.SaveChangesAsync();
        (await context.AiTaskRuns.Where(run => run.OperationAttempt > 0).SingleAsync()).OperationAttempt.Should().Be(2);
    }

    [PostgresFact]
    public async Task CompetingBatches_CannotReserveTheFinalFreezerUnitTwice()
    {
        // Past meals intentionally do not reserve current freezer stock.
        var date = DateOnly.FromDateTime(DateTime.Today).AddDays(7);
        var first = Candidate(date, "First");
        var second = Candidate(date.AddDays(1), "Second");
        await StoreBatch(first);
        await StoreBatch(second);

        var outcomes = await Task.WhenAll(Accept(first), Accept(second));

        outcomes.Select(result => result.Results.Single().Status)
            .Should().BeEquivalentTo(["created", "stockUnavailable"]);
        await using var scope = _provider!.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<DishhiveDbContext>()
            .PlannedMeals.CountAsync(meal => meal.FreezyItemRef == "last-lasagne"))
            .Should().Be(1);
    }

    [PostgresFact]
    public async Task CompetingRetries_ApplyOneSuggestionExactlyOnce()
    {
        var item = new AcceptMealSuggestionDto
        {
            Id = Guid.NewGuid(),
            Date = new DateOnly(2026, 10, 12),
            DishName = "Concurrent soup"
        };
        await StoreBatch(item);

        var outcomes = await Task.WhenAll(Accept(item), Accept(item));

        outcomes.Select(result => result.Results.Single().Status)
            .Should().BeEquivalentTo(["created", "alreadyApplied"]);
        await using var scope = _provider!.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<DishhiveDbContext>()
            .PlannedMeals.CountAsync(meal => meal.DishName == item.DishName)).Should().Be(1);
    }

    [PostgresFact]
    public async Task SourceUrlUniqueIndex_RejectsDuplicateImportedRecipes()
    {
        await using var scope = _provider!.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        const string sourceUrl = "https://example.test/unique-recipe";
        context.Recipes.AddRange(
            new Recipe { Title = "First", SourceUrl = sourceUrl },
            new Recipe { Title = "Second", SourceUrl = sourceUrl });

        var save = () => context.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    private static AcceptMealSuggestionDto Candidate(DateOnly date, string name) => new()
    {
        Id = Guid.NewGuid(),
        Date = date,
        DishName = name,
        FreezyItemRef = "last-lasagne",
        FreezyItemQuantity = 1
    };

    private async Task StoreBatch(AcceptMealSuggestionDto item)
    {
        await using var scope = _provider!.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MealSuggestionAcceptanceService>()
            .StoreBatchAsync(Guid.NewGuid(), PlanCompletionStatus.Complete,
            [
                new MealSuggestionDto
                {
                    Id = item.Id,
                    Date = item.Date,
                    MealType = item.MealType,
                    Course = item.Course,
                    AttendeeIds = item.AttendeeIds,
                    RecipeId = item.RecipeId,
                    DishName = item.DishName,
                    FreezyItemRef = item.FreezyItemRef,
                    FreezyItemQuantity = item.FreezyItemQuantity,
                    SourceUrl = item.SourceUrl,
                    CandidateVersion = item.CandidateVersion
                }
            ]);
    }

    private async Task<AcceptMealSuggestionsResponseDto> Accept(AcceptMealSuggestionDto item)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var batchId = await context.MealSuggestionCandidates
            .Where(candidate => candidate.Id == item.Id).Select(candidate => candidate.BatchId).SingleAsync();
        return await scope.ServiceProvider.GetRequiredService<MealSuggestionAcceptanceService>()
            .AcceptAsync(new AcceptMealSuggestionsRequestDto { BatchId = batchId, Suggestions = [item] });
    }
}

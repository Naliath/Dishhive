using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Facts;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Localization;
using Dishhive.Api.Services.Suggestions;
using Dishhive.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;

namespace Dishhive.Api.Tests.Crosscheck;

public class PersistenceCrossTests : IAsyncLifetime
{
    [PostgresFact]
    public async Task ConcurrentFreezerReassignment_RejectsStaleReleaseWithoutLosingMeal()
    {
        Guid id;
        await using (var setup = Context())
        {
            var meal = new PlannedMeal { Date = Day, DishName = "Retained dinner", FreezyItemRef = "original", FreezyItemQuantity = 1 };
            setup.PlannedMeals.Add(meal);
            await setup.SaveChangesAsync();
            id = meal.Id;
        }
        await using var stale = Context();
        var loaded = await stale.PlannedMeals.SingleAsync(meal => meal.Id == id);
        await using (var current = Context())
        {
            var changed = await current.PlannedMeals.SingleAsync(meal => meal.Id == id);
            changed.FreezyItemRef = "replacement";
            await current.SaveChangesAsync();
        }
        loaded.FreezyItemRef = null;
        loaded.FreezyItemQuantity = 0;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var check = Context();
        Assert.Equal("replacement", (await check.PlannedMeals.SingleAsync(meal => meal.Id == id)).FreezyItemRef);
    }
    private string _connection = null!;
    private ServiceProvider _provider = null!;
    private readonly SaveRace _race = new();
    // Keep reservation cases in the future as the calendar advances.
    private static readonly DateOnly Day = DateOnly.FromDateTime(DateTime.Today).AddDays(7);

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("DISHHIVE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured)) return;
        _connection = new NpgsqlConnectionStringBuilder(configured)
            { Database = $"dishhive_review_{Guid.NewGuid():N}" }.ConnectionString;
        var services = new ServiceCollection();
        services.AddDbContext<DishhiveDbContext>(options =>
            options.UseNpgsql(_connection).AddInterceptors(_race));
        _provider = services.BuildServiceProvider();
        await using var context = Context();
        await context.Database.MigrateAsync();
    }

    private DishhiveDbContext Context() => new(new DbContextOptionsBuilder<DishhiveDbContext>()
        .UseNpgsql(_connection).Options);

    private static MealSuggestionAcceptanceService Acceptance(DishhiveDbContext context)
    {
        var freezy = Substitute.For<IFreezyClient>();
        freezy.GetFrozenItemsAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<FrozenItem>>([
                new FrozenItem { Id = "stock", Name = "Soup", Quantity = 1 }
            ]));
        return new(context, Substitute.For<IRecipeImportService>(),
            new FreezerAvailabilityService(freezy, context, TestHouseholdClock.System), new AttendancePolicy(context),
            new UserMessageLocalizer(context), NullLogger<MealSuggestionAcceptanceService>.Instance);
    }

    private async Task<(Guid Batch, AcceptMealSuggestionDto Item)> Store(MealSuggestion suggestion, PlanningIntent? intent = null)
    {
        var batch = Guid.NewGuid();
        var item = new AcceptMealSuggestionDto
        {
            Id = Guid.NewGuid(), Date = suggestion.Date, DishName = suggestion.DishName!,
            MealType = suggestion.MealType, Course = suggestion.Course,
            RecipeId = suggestion.RecipeId, CandidateVersion = suggestion.CandidateVersion,
            AttendeeIds = suggestion.AttendeeIds.ToList(), FreezyItemRef = suggestion.FreezyItemRef,
            FreezyItemQuantity = suggestion.FreezyItemQuantity
        };
        await using var context = Context();
        await Acceptance(context).StoreBatchAsync(batch, PlanCompletionStatus.Complete,
        [new MealSuggestionDto
        {
            Id = item.Id, Date = item.Date, DishName = item.DishName, MealType = item.MealType,
            Course = item.Course, RecipeId = item.RecipeId, CandidateVersion = item.CandidateVersion,
            AttendeeIds = item.AttendeeIds, FreezyItemRef = item.FreezyItemRef,
            FreezyItemQuantity = item.FreezyItemQuantity, ConstraintIds = suggestion.ConstraintIds.ToList()
        }], intent: intent);
        return (batch, item);
    }

    private async Task<AcceptMealSuggestionResultDto> Accept(Guid batch, AcceptMealSuggestionDto item)
    {
        await using var context = Context();
        var response = await Acceptance(context).AcceptAsync(new()
            { BatchId = batch, Suggestions = [item] });
        return Assert.Single(response.Results);
    }

    [PostgresFact]
    public async Task RequiredCollectionMembership_IsRecheckedAtAcceptance()
    {
        var member = new FamilyMember { Name = "Member", IsActive = true };
        var recipe = new Recipe { Title = "Collection member" };
        var collection = new Cookbook { Name = "Required" };
        await using (var context = Context())
        {
            context.FamilyMembers.Add(member);
            context.Recipes.Add(recipe);
            collection.Entries.Add(new CookbookEntry { Cookbook = collection, Recipe = recipe });
            context.Cookbooks.Add(collection);
            await context.SaveChangesAsync();
        }
        var intent = new PlanningIntent { Constraints = [new PlanningConstraint
        {
            Id = "required", Dates = [Day], RequiredCollectionIds = [collection.Id.ToString()]
        }] };
        var suggestion = new MealSuggestion
        {
            Date = Day, DishName = recipe.Title, RecipeId = recipe.Id, CandidateVersion = "local:1",
            AttendeeIds = [member.Id], ConstraintIds = ["required"]
        };
        var control = await Store(suggestion, intent);
        Assert.Equal("created", (await Accept(control.Batch, control.Item)).Status);
        var changed = await Store(suggestion, intent);
        await using (var context = Context())
        {
            context.CookbookEntries.Remove(await context.CookbookEntries.SingleAsync());
            await context.SaveChangesAsync();
        }

        var result = await Accept(changed.Batch, changed.Item);

        Assert.Equal("needsReview", result.Status);
        Assert.Contains("collection", result.Error, StringComparison.OrdinalIgnoreCase);
        await using var verification = Context();
        Assert.Contains("collectionMembership", (await verification.AiReviewEvents.SingleAsync(review => review.BatchId == changed.Batch)).InputChangesJson);
    }

    [PostgresFact]
    public async Task UnchangedVersionedLocalTitle250AndExplicitGuest_AreAccepted()
    {
        var guest = new FamilyMember { Name = "Guest", IsGuest = true, IsActive = true };
        var recipe = new Recipe { Title = new string('R', 250) };
        await using (var context = Context())
        {
            context.FamilyMembers.Add(guest);
            context.Recipes.Add(recipe);
            await context.SaveChangesAsync();
        }
        var (batch, item) = await Store(new MealSuggestion
        {
            Date = Day, DishName = recipe.Title, RecipeId = recipe.Id,
            CandidateVersion = "local:1", AttendeeIds = [guest.Id]
        });
        Assert.Equal("created", (await Accept(batch, item)).Status);
        await using var verification = Context();
        Assert.Equal(guest.Id, (await verification.PlannedMealAttendees.SingleAsync()).FamilyMemberId);
        Assert.Equal(recipe.Title, (await verification.PlannedMeals.SingleAsync()).DishName);
    }

    [PostgresFact]
    public async Task ConcurrentRetriesOfSameFreezerSuggestion_AreIdempotent()
    {
        var (batch, item) = await Store(new MealSuggestion
        {
            Date = Day, DishName = "Soup", FreezyItemRef = "stock", FreezyItemQuantity = 1
        });
        var outcomes = await Task.WhenAll(Accept(batch, item), Accept(batch, item));
        Assert.Equal(new[] { "alreadyApplied", "created" }, outcomes.Select(r => r.Status).Order().ToArray());
    }

    [PostgresFact]
    public async Task UnchangedLocalFallback_MustRemainAcceptable()
    {
        var recipe = new Recipe { Title = new string('R', 250) };
        await using (var context = Context())
        {
            context.Recipes.Add(recipe);
            await context.SaveChangesAsync();
        }
        var planner = PlanningCrossTests.FailingPlanner();
        var suggestions = await planner.SuggestAsync(new MealSuggestionRequest
        {
            WeekStart = Day, DaysToFill = [Day],
            KnownRecipes = [new RecipeOption { Id = recipe.Id, Title = recipe.Title, Revision = recipe.Revision }],
            Favorites = [new FavoriteDish { MemberName = "Alex", RecipeId = recipe.Id, DishName = recipe.Title }]
        });
        var (batch, item) = await Store(Assert.Single(suggestions));
        var result = await Accept(batch, item);
        Assert.True(result.Status == "created", $"Actual: {result.Status}: {result.Error}");
    }

    [PostgresFact]
    public async Task SelectedGuest_MustSurviveRulesFallbackAndAcceptance()
    {
        var household = new FamilyMember { Name = "Alex", IsActive = true };
        var guest = new FamilyMember { Name = "Guest", IsGuest = true, IsActive = true };
        await using (var context = Context())
        {
            context.FamilyMembers.AddRange(household, guest);
            await context.SaveChangesAsync();
        }
        var planner = PlanningCrossTests.FailingPlanner();
        var suggestions = await planner.SuggestAsync(new MealSuggestionRequest
        {
            WeekStart = Day, DaysToFill = [Day],
            Members = [new MemberProfile { Id = household.Id, Name = household.Name },
                new MemberProfile { Id = guest.Id, Name = guest.Name }],
            Favorites = [new FavoriteDish { MemberName = "Alex", DishName = "Soup" }]
        });
        var (batch, item) = await Store(Assert.Single(suggestions));
        var result = await Accept(batch, item);
        Assert.Equal("created", result.Status);
        await using var verification = Context();
        var attendeeIds = await verification.PlannedMealAttendees.Select(a => a.FamilyMemberId).ToListAsync();
        Assert.Contains(guest.Id, attendeeIds);
    }

    [PostgresFact]
    public async Task ChangedFactsConfirmation_MustRequireReviewOnAcceptance()
    {
        var recipe = new Recipe { Title = "Soup", DietaryFactsStatus = DietaryFactsStatus.UserConfirmed,
            DietaryFactsAssessedAt = DateTime.UtcNow, DietaryFactsAssessedRevision = 1 };
        await using (var context = Context())
        {
            context.Recipes.Add(recipe);
            await context.SaveChangesAsync();
        }
        var (batch, item) = await Store(new MealSuggestion
        {
            Date = Day, DishName = recipe.Title, RecipeId = recipe.Id, CandidateVersion = "local:1"
        });
        await using (var context = Context())
        {
            var current = await context.Recipes.Include(r => r.DietaryFacts).SingleAsync();
            RecipeFactsStateService.Replace(context, current, [IngredientClass.Milk], DietaryFactsStatus.UserConfirmed);
            await context.SaveChangesAsync();
        }
        var result = await Accept(batch, item);
        Assert.True(result.Status == "needsReview", $"Actual: {result.Status}");
    }

    [PostgresFact]
    public async Task ClientCannotDropReviewedFreezerReservation()
    {
        var (batch, item) = await Store(new MealSuggestion
        {
            Date = Day, DishName = "Soup", FreezyItemRef = "stock", FreezyItemQuantity = 1
        });
        item.FreezyItemRef = null;
        item.FreezyItemQuantity = 0;
        var result = await Accept(batch, item);
        Assert.True(result.Status == "needsReview", $"Actual: {result.Status}");
    }

    [PostgresFact]
    public async Task LegacyBatchWithoutReviewSnapshot_MustRequireReview()
    {
        var (batch, item) = await Store(new MealSuggestion { Date = Day, DishName = "Soup" });
        await using (var context = Context())
        {
            var candidate = await context.MealSuggestionCandidates.SingleAsync();
            candidate.ReviewSnapshotJson = null;
            await context.SaveChangesAsync();
        }
        Assert.Equal("needsReview", (await Accept(batch, item)).Status);
    }

    [PostgresFact]
    public async Task ChangedAttendance_MustRequireReview()
    {
        var member = new FamilyMember { Name = "Alex", IsActive = true };
        await using (var context = Context())
        {
            context.FamilyMembers.Add(member);
            await context.SaveChangesAsync();
        }
        var (batch, item) = await Store(new MealSuggestion { Date = Day, DishName = "Soup", AttendeeIds = [member.Id] });
        item.AttendeeIds = [];
        Assert.Equal("needsReview", (await Accept(batch, item)).Status);
    }

    [PostgresFact]
    public async Task ChangedAttendeeAllergy_MustRequireReview()
    {
        var member = new FamilyMember { Name = "Alex", IsActive = true };
        var tag = new DietaryTag { Name = "Milk", Kind = DietaryTagKind.Allergy };
        await using (var context = Context())
        {
            context.FamilyMembers.Add(member);
            context.DietaryTags.Add(tag);
            await context.SaveChangesAsync();
        }
        var (batch, item) = await Store(new MealSuggestion { Date = Day, DishName = "Soup", AttendeeIds = [member.Id] });
        await using (var context = Context())
        {
            context.FamilyMemberDietaryTags.Add(new FamilyMemberDietaryTag
            {
                FamilyMemberId = member.Id, DietaryTagId = tag.Id, ExcludedClasses = [IngredientClass.Milk]
            });
            await context.SaveChangesAsync();
        }
        Assert.Equal("needsReview", (await Accept(batch, item)).Status);
    }

    [PostgresFact]
    public async Task LeaseExpiringAfterStartup_MustBeRecoveredByRunningWorker()
    {
        await using (var context = Context())
        {
            var recipe = new Recipe { Title = "Soup", Ingredients = [new RecipeIngredient { Name = "water" }] };
            context.Recipes.Add(recipe);
            context.RecipeEnrichmentJobs.Add(new RecipeEnrichmentJob
            {
                Recipe = recipe, RecipeRevision = 1, OperationId = "crash",
                State = RecipeEnrichmentJobState.Running, LeaseExpiresAt = DateTime.UtcNow.AddMilliseconds(300)
            });
            await context.SaveChangesAsync();
        }
        using var worker = Worker();
        await worker.RecoverExpiredLeasesAsync();
        await Task.Delay(500);
        Assert.True(await worker.ProcessNextAsync(), "The expired running lease remains permanently stranded");
    }

    [PostgresFact]
    public async Task HumanConfirmationBetweenWorkerReadAndCommit_MustNotBeOverwritten()
    {
        Guid recipeId;
        await using (var context = Context())
        {
            var recipe = new Recipe { Title = "Soup", Ingredients = [new RecipeIngredient { Name = "water" }] };
            context.Recipes.Add(recipe);
            await context.SaveChangesAsync();
            recipeId = recipe.Id;
        }
        using var worker = Worker();
        await worker.EnqueueAsync(recipeId);
        _race.BeforeAiSave = async () =>
        {
            await using var humanContext = Context();
            var recipe = await humanContext.Recipes.Include(r => r.DietaryFacts).SingleAsync();
            RecipeFactsStateService.Replace(humanContext, recipe, [], DietaryFactsStatus.UserConfirmed);
            await humanContext.SaveChangesAsync();
        };
        await worker.ProcessNextAsync();
        await using var verification = Context();
        var saved = await verification.Recipes.SingleAsync();
        Assert.True(saved.DietaryFactsStatus == DietaryFactsStatus.UserConfirmed,
            $"Actual: {saved.DietaryFactsStatus}; confirmation version {saved.FactsConfirmationVersion}");
    }

    [PostgresFact]
    public async Task Recovery_CoalescesMultipleExpiredLeasesToNewestRevision()
    {
        await using (var context = Context())
        {
            var recipe = new Recipe { Title = "Soup", Revision = 2 };
            context.Recipes.Add(recipe);
            foreach (var revision in new long[] { 1, 2 })
                context.RecipeEnrichmentJobs.Add(new RecipeEnrichmentJob
                {
                    Recipe = recipe, RecipeRevision = revision, OperationId = "restart",
                    State = RecipeEnrichmentJobState.Running, LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1)
                });
            await context.SaveChangesAsync();
        }
        using var worker = Worker();
        await worker.RecoverExpiredLeasesAsync();
        await using var verification = Context();
        var jobs = await verification.RecipeEnrichmentJobs.ToListAsync();
        Assert.Equal(2, Assert.Single(jobs, job => job.State == RecipeEnrichmentJobState.Pending).RecipeRevision);
        Assert.Single(jobs, job => job.State == RecipeEnrichmentJobState.Superseded);
    }

    [PostgresFact]
    public async Task ConcurrentWorkers_ClaimAJobOnlyOnce()
    {
        Guid recipeId;
        await using (var context = Context())
        {
            var recipe = new Recipe { Title = "Soup", Ingredients = [new RecipeIngredient { Name = "water" }] };
            context.Recipes.Add(recipe);
            await context.SaveChangesAsync();
            recipeId = recipe.Id;
        }
        using var first = Worker();
        using var second = Worker();
        await first.EnqueueAsync(recipeId);
        var bothRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        _race.BeforeClaimSave = async () =>
        {
            if (Interlocked.Increment(ref reads) == 2) bothRead.TrySetResult();
            await bothRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
        };
        var processed = await Task.WhenAll(first.ProcessNextAsync(), second.ProcessNextAsync());
        Assert.Single(processed, value => value);
        await using var verification = Context();
        var job = await verification.RecipeEnrichmentJobs.SingleAsync();
        Assert.Equal(1, job.AttemptCount);
        Assert.Equal(RecipeEnrichmentJobState.Completed, job.State);
    }

    [PostgresFact]
    public async Task TranslationCannotCommitOverConcurrentSourceEdit()
    {
        await using (var context = Context())
        {
            var recipe = new Recipe { Title = "Soup", Ingredients = [new RecipeIngredient { Name = "water" }] };
            context.Recipes.Add(recipe);
            context.RecipeEnrichmentJobs.Add(new RecipeEnrichmentJob
            {
                Recipe = recipe, RecipeRevision = 1, OperationId = "translate", TargetLanguage = "nl",
                Task = RecipeEnrichmentTask.Translation
            });
            await context.SaveChangesAsync();
        }
        var localizer = Substitute.For<IRecipeLocalizationService>();
        localizer.TranslateAsync(Arg.Any<Recipe>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string>())
            .Returns(Task.FromResult<LocalizedRecipeText?>(new("Soep", null, ["water"], [])));
        using var worker = new RecipeFactsAssessmentService(_provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IRecipeFactsExtractor>(), localizer, new SupportedLanguageCatalog(),
            NullLogger<RecipeFactsAssessmentService>.Instance);
        _race.BeforeTranslationSave = async () =>
        {
            await using var edit = Context();
            var current = await edit.Recipes.SingleAsync();
            current.Title = "Edited soup";
            current.Revision++;
            await edit.SaveChangesAsync();
        };
        await worker.ProcessNextAsync();
        await using var verification = Context();
        Assert.Equal("Edited soup", (await verification.Recipes.SingleAsync()).Title);
        Assert.Equal(RecipeEnrichmentJobState.Superseded, (await verification.RecipeEnrichmentJobs.SingleAsync()).State);
    }

    [PostgresFact]
    public async Task TranslationChangingOnlyChildRowsCannotCommitOverConcurrentSourceEdit()
    {
        await using (var context = Context())
        {
            var recipe = new Recipe
            {
                Title = "Soup", OriginalTitle = "Soup", ContentLanguage = "nl",
                Ingredients = [new RecipeIngredient { Name = "onion" }],
                Steps = [new RecipeStep { StepNumber = 1, Instruction = "Chop", OriginalInstruction = "Chop" }]
            };
            context.Recipes.Add(recipe);
            context.RecipeEnrichmentJobs.Add(new RecipeEnrichmentJob
            {
                Recipe = recipe, RecipeRevision = 1, OperationId = "translate-children", TargetLanguage = "nl",
                Task = RecipeEnrichmentTask.Translation
            });
            await context.SaveChangesAsync();
        }
        var localizer = Substitute.For<IRecipeLocalizationService>();
        localizer.TranslateAsync(Arg.Any<Recipe>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string>())
            .Returns(Task.FromResult<LocalizedRecipeText?>(new("Soup", null, ["ui"], ["Snij"])));
        using var worker = new RecipeFactsAssessmentService(_provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IRecipeFactsExtractor>(), localizer, new SupportedLanguageCatalog(),
            NullLogger<RecipeFactsAssessmentService>.Instance);
        _race.BeforeTranslationSave = async () =>
        {
            await using var edit = Context();
            var current = await edit.Recipes.Include(recipe => recipe.Ingredients)
                .Include(recipe => recipe.Steps).SingleAsync();
            current.Ingredients.Single().Name = "garlic";
            current.Steps.Single().Instruction = "Edited step";
            current.Revision++;
            await edit.SaveChangesAsync();
        };
        await worker.ProcessNextAsync();
        await using var verification = Context();
        Assert.Equal("garlic", (await verification.RecipeIngredients.SingleAsync()).Name);
        Assert.Equal("Edited step", (await verification.RecipeSteps.SingleAsync()).Instruction);
        Assert.Equal(RecipeEnrichmentJobState.Superseded, (await verification.RecipeEnrichmentJobs.SingleAsync()).State);
    }

    [PostgresFact]
    public async Task MetricsRecovery_PurgesExpiredTextAndOnlyAbandonsInterruptedCalls()
    {
        var old = DateTime.UtcNow.AddDays(-2);
        var recent = DateTime.UtcNow;
        await using (var context = Context())
        {
            context.AiTaskRuns.AddRange(
                new AiTaskRun { OperationId = "interrupted", StartedAt = old, CompletedAt = old, Outcome = "responseReceived" },
                new AiTaskRun { OperationId = "finished", StartedAt = old, CompletedAt = old, Outcome = "success" },
                new AiTaskRun { OperationId = "active", StartedAt = recent, CompletedAt = recent, Outcome = "responseReceived" });
            context.AiPlanningRuns.AddRange(
                new AiPlanningRun { RequestId = "old", StartedAt = old, CompletedAt = old, Instructions = "old text", NormalizedIntentJson = "{}" },
                new AiPlanningRun { RequestId = "recent", StartedAt = recent, CompletedAt = recent, Instructions = "recent text", NormalizedIntentJson = "{}" });
            await context.SaveChangesAsync();
        }
        var options = new AiOptions { RetainDiagnosticText = true };
        using var maintenance = new AiMetricsMaintenanceService(_provider.GetRequiredService<IServiceScopeFactory>(),
            options, NullLogger<AiMetricsMaintenanceService>.Instance);
        await maintenance.RunOnceAsync();
        await using (var verification = Context())
        {
            var calls = await verification.AiTaskRuns.ToDictionaryAsync(run => run.OperationId);
            Assert.Equal("abandoned", calls["interrupted"].Outcome);
            Assert.Equal("success", calls["finished"].Outcome);
            Assert.Equal("responseReceived", calls["active"].Outcome);
            var plans = await verification.AiPlanningRuns.ToDictionaryAsync(run => run.RequestId);
            Assert.Null(plans["old"].Instructions);
            Assert.Null(plans["old"].NormalizedIntentJson);
            Assert.Equal("recent text", plans["recent"].Instructions);
        }
        options.RetainDiagnosticText = false;
        await maintenance.RunOnceAsync();
        await using var final = Context();
        Assert.False(await final.AiPlanningRuns.AnyAsync(run => run.Instructions != null || run.NormalizedIntentJson != null));
    }

    private RecipeFactsAssessmentService Worker()
    {
        var extractor = Substitute.For<IRecipeFactsExtractor>();
        extractor.IsAvailable.Returns(true);
        extractor.ExtractAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<string>())
            .Returns(Task.FromResult<IReadOnlyList<IngredientClass>?>([IngredientClass.Milk]));
        return new(_provider.GetRequiredService<IServiceScopeFactory>(), extractor,
            NullLogger<RecipeFactsAssessmentService>.Instance);
    }

    private sealed class SaveRace : SaveChangesInterceptor
    {
        public Func<Task>? BeforeAiSave;
        public Func<Task>? BeforeClaimSave;
        public Func<Task>? BeforeTranslationSave;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (BeforeClaimSave is { } claim && eventData.Context!.ChangeTracker.Entries<RecipeEnrichmentJob>()
                .Any(entry => entry.Entity.State == RecipeEnrichmentJobState.Running)) await claim();
            if (BeforeTranslationSave is { } translation && eventData.Context!.ChangeTracker.Entries<Recipe>()
                .Any(entry => entry.Entity.ContentLanguage == "nl"))
            {
                BeforeTranslationSave = null;
                await translation();
            }
            if (BeforeAiSave is { } action && eventData.Context!.ChangeTracker.Entries<Recipe>()
                .Any(r => r.Entity.DietaryFactsStatus == DietaryFactsStatus.AiDetected))
            {
                BeforeAiSave = null;
                await action();
            }
            return result;
        }
    }

    public async Task DisposeAsync()
    {
        if (_provider is null) return;
        await using var context = Context();
        await context.Database.EnsureDeletedAsync();
        await _provider.DisposeAsync();
    }
}

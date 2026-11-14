using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Localization;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dishhive.Api.Tests.Services;

public class MealSuggestionAcceptanceServiceTests : IDisposable
{
    [Theory]
    [InlineData(false, "created")]
    [InlineData(true, "needsReview")]
    public async Task SemanticDishApprovalSurvivesReviewButCannotApproveChangedRecipeContent(bool changeContent, string expected)
    {
        var date = new DateOnly(2026, 7, 13);
        var recipe = new Recipe { Title = "Crepes", DietaryFactsStatus = DietaryFactsStatus.AiDetected,
            TotalTimeMinutes = 15 };
        _context.Recipes.Add(recipe);
        await _context.SaveChangesAsync();
        var version = SuggestionCandidateVersion.ForLocal(recipe);
        var original = new MealSuggestion { RecipeId = recipe.Id, DishName = recipe.Title, CandidateVersion = version };
        var id = Guid.NewGuid();
        var batch = Guid.NewGuid();
        var intent = new PlanningIntent { Constraints = [new()
        {
            Id = "food", Dates = [date], DishRequest = "pannekoeken", RequiresDishMatch = true,
            VerifiedDishVersions = [PlanningDishIdentity.Key(original)]
        }] };
        await _service.StoreBatchAsync(batch, PlanCompletionStatus.Partial, [new()
        {
            Id = id, Date = date, RecipeId = recipe.Id, DishName = recipe.Title,
            CandidateVersion = version, ConstraintIds = ["food"], FactsAssessed = true, TotalTimeMinutes = 15
        }], intent: intent);
        if (changeContent)
        {
            recipe.Title = "Meatballs"; // Even a mutation that forgot to advance Revision must invalidate the proof.
            await _context.SaveChangesAsync();
        }
        var response = await _service.AcceptAsync(new()
        {
            BatchId = batch, Suggestions = [new() { Id = id, Date = date, RecipeId = recipe.Id, DishName = "Crepes", CandidateVersion = version }]
        });
        Assert.Equal(expected, Assert.Single(response.Results).Status);
        Assert.Equal(changeContent ? 0 : 1, await _context.PlannedMeals.CountAsync());
    }

    private readonly DishhiveDbContext _context;
    private readonly IRecipeImportService _importService = Substitute.For<IRecipeImportService>();
    private readonly MealSuggestionAcceptanceService _service;

    public MealSuggestionAcceptanceServiceTests()
    {
        _context = new DishhiveDbContext(new DbContextOptionsBuilder<DishhiveDbContext>()
            .UseInMemoryDatabase($"Acceptance_{Guid.NewGuid()}")
            .Options);
        var freezy = Substitute.For<IFreezyClient>();
        freezy.GetFrozenItemsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FrozenItem>>([]));
        _service = new MealSuggestionAcceptanceService(
            _context,
            _importService,
            new FreezerAvailabilityService(freezy, _context, TestHouseholdClock.System),
            new AttendancePolicy(_context),
            new UserMessageLocalizer(_context),
            NullLogger<MealSuggestionAcceptanceService>.Instance);
    }

    [Fact]
    public async Task Accept_KnownRecipe_ReplacesIdeaAndIsIdempotent()
    {
        var date = new DateOnly(2026, 7, 13);
        var recipe = new Recipe { Title = "Lasagne" };
        _context.Recipes.Add(recipe);
        _context.PlannedMeals.Add(new PlannedMeal
        {
            Date = date,
            MealType = MealType.Dinner,
            Course = Course.Main,
            VagueInstruction = "something Italian"
        });
        await _context.SaveChangesAsync();
        var request = await RequestAsync(new AcceptMealSuggestionDto
        {
            Id = Guid.NewGuid(),
            Date = date,
            RecipeId = recipe.Id,
            DishName = recipe.Title
        });

        var first = await _service.AcceptAsync(request);
        var second = await _service.AcceptAsync(request);

        first.Results.Should().ContainSingle().Which.Status.Should().Be("created");
        second.Results.Should().ContainSingle().Which.Status.Should().Be("alreadyApplied");
        var meal = await _context.PlannedMeals.SingleAsync();
        meal.RecipeId.Should().Be(recipe.Id);
        meal.DishName.Should().Be(recipe.Title);
        meal.VagueInstruction.Should().BeNull();
    }

    [Fact]
    public async Task Accept_ExternalImportFails_PreservesIdea()
    {
        var date = new DateOnly(2026, 7, 13);
        _context.PlannedMeals.Add(new PlannedMeal
        {
            Date = date,
            MealType = MealType.Dinner,
            Course = Course.Main,
            VagueInstruction = "from Dagelijkse Kost"
        });
        await _context.SaveChangesAsync();
        _importService.ImportAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<Recipe>>(_ => throw new RecipeExtractionFailedException("No recipe found"));

        var response = await _service.AcceptAsync(await RequestAsync(new AcceptMealSuggestionDto
        {
            Id = Guid.NewGuid(),
            Date = date,
            DishName = "External dish",
            SourceUrl = "https://example.com/dish"
        }));

        response.Results.Should().ContainSingle().Which.Status.Should().Be("importFailed");
        var idea = await _context.PlannedMeals.SingleAsync();
        idea.VagueInstruction.Should().Be("from Dagelijkse Kost");
    }

    [Fact]
    public async Task Accept_UnexpectedExternalImportFailure_IsIsolatedToThatSuggestion()
    {
        var failed = new AcceptMealSuggestionDto
        {
            Id = Guid.NewGuid(),
            Date = new DateOnly(2026, 7, 13),
            DishName = "Broken external dish",
            SourceUrl = "https://example.com/broken"
        };
        var valid = new AcceptMealSuggestionDto
        {
            Id = Guid.NewGuid(),
            Date = new DateOnly(2026, 7, 14),
            DishName = "Soup"
        };
        _importService.ImportAsync(failed.SourceUrl, Arg.Any<CancellationToken>())
            .Returns<Task<Recipe>>(_ => throw new InvalidOperationException("Unexpected importer failure"));

        var request = new AcceptMealSuggestionsRequestDto
        {
            BatchId = Guid.NewGuid(),
            Suggestions = [failed, valid]
        };
        await StoreRequestAsync(request);
        var response = await _service.AcceptAsync(request);

        response.Results.Should().Contain(result =>
            result.SuggestionId == failed.Id && result.Status == "importFailed");
        response.Results.Should().Contain(result =>
            result.SuggestionId == valid.Id && result.Status == "created");
        (await _context.PlannedMeals.SingleAsync()).DishName.Should().Be(valid.DishName);
    }

    [Fact]
    public async Task Accept_DuplicateSuggestionInOneRequest_AppliesOnce()
    {
        var item = new AcceptMealSuggestionDto
        {
            Id = Guid.NewGuid(),
            Date = new DateOnly(2026, 7, 13),
            DishName = "Soup"
        };
        var request = new AcceptMealSuggestionsRequestDto
        {
            BatchId = Guid.NewGuid(),
            Suggestions = [item, item]
        };
        await StoreRequestAsync(request);

        var response = await _service.AcceptAsync(request);

        response.Results.Should().ContainSingle();
        (await _context.PlannedMeals.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Accept_FreezerStockNoLongerAvailable_DoesNotCreateMeal()
    {
        var response = await _service.AcceptAsync(await RequestAsync(new AcceptMealSuggestionDto
        {
            Id = Guid.NewGuid(),
            Date = new DateOnly(2026, 7, 13),
            DishName = "Frozen soup",
            FreezyItemRef = "gone-item",
            FreezyItemQuantity = 1
        }));

        response.Results.Should().ContainSingle().Which.Status.Should().Be("stockUnavailable");
        (await _context.PlannedMeals.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Accept_ExternalSuggestion_ImportsOnceAcrossRetry()
    {
        var recipe = new Recipe { Title = "Imported soup", SourceUrl = "https://example.com/soup" };
        _context.Recipes.Add(recipe);
        await _context.SaveChangesAsync();
        _importService.ImportAsync(recipe.SourceUrl, Arg.Any<CancellationToken>()).Returns(recipe);
        var request = await RequestAsync(new AcceptMealSuggestionDto
        {
            Id = Guid.NewGuid(),
            Date = new DateOnly(2026, 7, 13),
            DishName = "Model title",
            SourceUrl = recipe.SourceUrl
        });

        var first = await _service.AcceptAsync(request);
        var second = await _service.AcceptAsync(request);

        first.Results.Single().Status.Should().Be("created");
        second.Results.Single().Status.Should().Be("alreadyApplied");
        await _importService.Received(1).ImportAsync(recipe.SourceUrl, Arg.Any<CancellationToken>());
        (await _context.PlannedMeals.SingleAsync()).DishName.Should().Be(recipe.Title);
    }

    [Fact]
    public async Task Accept_DessertForSelectedAttendee_PreservesCourseAndAudience()
    {
        var selected = new FamilyMember { Name = "Alex", IsActive = true };
        var other = new FamilyMember { Name = "Naomi", IsActive = true };
        _context.FamilyMembers.AddRange(selected, other);
        await _context.SaveChangesAsync();

        var response = await _service.AcceptAsync(await RequestAsync(new AcceptMealSuggestionDto
        {
            Id = Guid.NewGuid(),
            Date = new DateOnly(2026, 7, 17),
            MealType = MealType.Dinner,
            Course = Course.Dessert,
            AttendeeIds = [selected.Id],
            DishName = "Fruit tart"
        }));

        response.Results.Single().Status.Should().Be("created");
        var meal = await _context.PlannedMeals.Include(item => item.Attendees).SingleAsync();
        meal.MealType.Should().Be(MealType.Dinner);
        meal.Course.Should().Be(Course.Dessert);
        meal.Attendees.Select(attendee => attendee.FamilyMemberId).Should().Equal(selected.Id);
    }

    [Fact]
    public async Task Accept_ExplicitGuest_PreservesGuestAttendance()
    {
        var household = new FamilyMember { Name = "Alex", IsActive = true };
        var guest = new FamilyMember { Name = "Guest", IsActive = true, IsGuest = true };
        _context.FamilyMembers.AddRange(household, guest);
        await _context.SaveChangesAsync();

        var response = await _service.AcceptAsync(await RequestAsync(new AcceptMealSuggestionDto
        {
            Id = Guid.NewGuid(), Date = new DateOnly(2026, 7, 17), DishName = "Shared dinner",
            AttendeeIds = [household.Id, guest.Id]
        }));

        response.Results.Single().Status.Should().Be("created");
        var meal = await _context.PlannedMeals.Include(item => item.Attendees).SingleAsync();
        meal.Attendees.Select(item => item.FamilyMemberId)
            .Should().BeEquivalentTo([household.Id, guest.Id]);
    }

    [Fact]
    public async Task Accept_GuestOnly_PreservesGuestAttendance()
    {
        var guest = new FamilyMember { Name = "Guest", IsActive = true, IsGuest = true };
        _context.FamilyMembers.Add(guest);
        await _context.SaveChangesAsync();

        var response = await _service.AcceptAsync(await RequestAsync(new AcceptMealSuggestionDto
        {
            Id = Guid.NewGuid(), Date = new DateOnly(2026, 7, 17), DishName = "Guest dinner",
            AttendeeIds = [guest.Id]
        }));

        response.Results.Single().Status.Should().Be("created");
        (await _context.PlannedMealAttendees.SingleAsync()).FamilyMemberId.Should().Be(guest.Id);
    }

    [Fact]
    public async Task Accept_InactiveExplicitMember_FailsWithoutSubstitution()
    {
        var inactive = new FamilyMember { Name = "Inactive", IsActive = false };
        _context.FamilyMembers.Add(inactive);
        await _context.SaveChangesAsync();

        var response = await _service.AcceptAsync(await RequestAsync(new AcceptMealSuggestionDto
        {
            Id = Guid.NewGuid(), Date = new DateOnly(2026, 7, 17), DishName = "Dinner",
            AttendeeIds = [inactive.Id]
        }));

        response.Results.Single().Status.Should().Be("planningFailed");
        (await _context.PlannedMeals.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Accept_RecordsAcceptedRejectedAndModifiedReviewSignals()
    {
        var batchId = Guid.NewGuid();
        var accepted = new MealSuggestionDto
        {
            Id = Guid.NewGuid(), Date = new DateOnly(2026, 7, 17), DishName = "Soup"
        };
        var rejected = new MealSuggestionDto
        {
            Id = Guid.NewGuid(), Date = new DateOnly(2026, 7, 18), DishName = "Stew"
        };
        await _service.StoreBatchAsync(batchId, PlanCompletionStatus.Complete, [accepted, rejected]);

        await _service.AcceptAsync(new AcceptMealSuggestionsRequestDto
        {
            BatchId = batchId,
            RejectedSuggestionIds = [rejected.Id],
            Suggestions =
            [
                new AcceptMealSuggestionDto
                {
                    Id = accepted.Id, Date = accepted.Date, DishName = accepted.DishName
                }
            ]
        });

        var outcomes = await _context.MealSuggestionCandidates.AsNoTracking()
            .ToDictionaryAsync(candidate => candidate.Id);
        outcomes[accepted.Id].ReviewOutcome.Should().Be("accepted");
        outcomes[rejected.Id].ReviewOutcome.Should().Be("rejected");
        outcomes.Values.Should().OnlyContain(candidate => candidate.ResolvedAt != null);
    }

    private async Task<AcceptMealSuggestionsRequestDto> RequestAsync(AcceptMealSuggestionDto item)
    {
        var request = new AcceptMealSuggestionsRequestDto { BatchId = Guid.NewGuid(), Suggestions = [item] };
        await StoreRequestAsync(request);
        return request;
    }

    private async Task StoreRequestAsync(AcceptMealSuggestionsRequestDto request)
    {
        foreach (var item in request.Suggestions.Where(item => item.RecipeId.HasValue))
            item.CandidateVersion = SuggestionCandidateVersion.ForLocal(
                (await _context.Recipes.FindAsync(item.RecipeId!.Value))!.Revision);
        await _service.StoreBatchAsync(request.BatchId, PlanCompletionStatus.Complete,
            request.Suggestions.DistinctBy(item => item.Id).Select(item => new MealSuggestionDto
            {
                Id = item.Id, Date = item.Date, MealType = item.MealType, Course = item.Course,
                RecipeId = item.RecipeId, DishName = item.DishName, SourceUrl = item.SourceUrl,
                AttendeeIds = item.AttendeeIds, CandidateVersion = item.CandidateVersion,
                FreezyItemRef = item.FreezyItemRef, FreezyItemQuantity = item.FreezyItemQuantity
            }).ToList());
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}

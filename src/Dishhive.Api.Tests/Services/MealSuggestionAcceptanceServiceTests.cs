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
            new FreezerAvailabilityService(freezy, _context),
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
        var request = Request(new AcceptMealSuggestionDto
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

        var response = await _service.AcceptAsync(Request(new AcceptMealSuggestionDto
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

        var response = await _service.AcceptAsync(new AcceptMealSuggestionsRequestDto
        {
            BatchId = Guid.NewGuid(),
            Suggestions = [failed, valid]
        });

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

        var response = await _service.AcceptAsync(request);

        response.Results.Should().ContainSingle();
        (await _context.PlannedMeals.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Accept_FreezerStockNoLongerAvailable_DoesNotCreateMeal()
    {
        var response = await _service.AcceptAsync(Request(new AcceptMealSuggestionDto
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
        var request = Request(new AcceptMealSuggestionDto
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

        var response = await _service.AcceptAsync(Request(new AcceptMealSuggestionDto
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

    private static AcceptMealSuggestionsRequestDto Request(AcceptMealSuggestionDto item) => new()
    {
        BatchId = Guid.NewGuid(),
        Suggestions = [item]
    };

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}

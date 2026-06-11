using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

public class RulesMealSuggestionServiceTests
{
    private static readonly DateOnly WeekStart = new(2026, 6, 15); // a Monday
    private static readonly DateOnly Today = WeekStart;

    private readonly RulesMealSuggestionService _service = new();

    private static MealSuggestionRequest Request(
        IReadOnlyList<DateOnly>? daysToFill = null,
        IReadOnlyList<FavoriteDish>? favorites = null,
        IReadOnlyList<DishHistoryEntry>? recentDishes = null,
        IReadOnlyList<FrozenItem>? frozenItems = null,
        IReadOnlyList<RecipeOption>? recipes = null) => new()
    {
        WeekStart = WeekStart,
        DaysToFill = daysToFill ?? [WeekStart, WeekStart.AddDays(1), WeekStart.AddDays(2)],
        Favorites = favorites ?? [],
        RecentDishes = recentDishes ?? [],
        AvailableFrozenItems = frozenItems ?? [],
        KnownRecipes = recipes ?? []
    };

    [Fact]
    public async Task Suggest_ExpiringFreezerItem_IsSlottedFirst()
    {
        var request = Request(
            frozenItems:
            [
                new FrozenItem
                {
                    Id = "1", Name = "Frozen lasagna", Quantity = 1,
                    ExpirationDate = Today.AddDays(3).ToDateTime(TimeOnly.MinValue)
                }
            ],
            favorites: [new FavoriteDish { MemberName = "Anna", DishName = "Spaghetti" }]);

        var suggestions = await _service.SuggestAsync(request);

        suggestions[0].DishName.Should().Be("Frozen lasagna");
        suggestions[0].Reason.Should().Contain("expires");
    }

    [Fact]
    public async Task Suggest_FreezerItemFarFromExpiry_IsNotForced()
    {
        var request = Request(
            frozenItems:
            [
                new FrozenItem
                {
                    Id = "1", Name = "Frozen soup", Quantity = 1,
                    ExpirationDate = Today.AddDays(120).ToDateTime(TimeOnly.MinValue)
                }
            ]);

        var suggestions = await _service.SuggestAsync(request);

        suggestions.Should().NotContain(s => s.DishName == "Frozen soup");
    }

    [Fact]
    public async Task Suggest_RecentlyPlannedFavorite_IsExcluded()
    {
        var request = Request(
            favorites: [new FavoriteDish { MemberName = "Anna", DishName = "Spaghetti" }],
            recentDishes:
            [
                new DishHistoryEntry { DishName = "Spaghetti", TimesPlanned = 3, LastPlanned = Today.AddDays(-3) }
            ]);

        var suggestions = await _service.SuggestAsync(request);

        suggestions.Should().BeEmpty();
    }

    [Fact]
    public async Task Suggest_LowRatedFavorite_IsExcluded()
    {
        var request = Request(
            favorites: [new FavoriteDish { MemberName = "Tom", DishName = "Stamppot" }],
            recentDishes:
            [
                new DishHistoryEntry
                {
                    DishName = "Stamppot", TimesPlanned = 2,
                    LastPlanned = Today.AddDays(-60), AverageRating = 2.0
                }
            ]);

        var suggestions = await _service.SuggestAsync(request);

        suggestions.Should().BeEmpty();
    }

    [Fact]
    public async Task Suggest_LovedFavorites_ArePreferred()
    {
        var request = Request(
            daysToFill: [WeekStart],
            favorites:
            [
                new FavoriteDish { MemberName = "Anna", DishName = "Ok dish" },
                new FavoriteDish { MemberName = "Anna", DishName = "Loved dish" }
            ],
            recentDishes:
            [
                new DishHistoryEntry { DishName = "Ok dish", LastPlanned = Today.AddDays(-30), AverageRating = 3.5 },
                new DishHistoryEntry { DishName = "Loved dish", LastPlanned = Today.AddDays(-30), AverageRating = 4.8 }
            ]);

        var suggestions = await _service.SuggestAsync(request);

        suggestions.Should().ContainSingle();
        suggestions[0].DishName.Should().Be("Loved dish");
        suggestions[0].Reason.Should().Contain("4.8");
    }

    [Fact]
    public async Task Suggest_RoundRobinsAcrossMembers()
    {
        var request = Request(
            daysToFill: [WeekStart, WeekStart.AddDays(1)],
            favorites:
            [
                new FavoriteDish { MemberName = "Anna", DishName = "Anna 1" },
                new FavoriteDish { MemberName = "Anna", DishName = "Anna 2" },
                new FavoriteDish { MemberName = "Tom", DishName = "Tom 1" }
            ]);

        var suggestions = await _service.SuggestAsync(request);

        suggestions.Should().HaveCount(2);
        suggestions.Select(s => s.DishName).Should().Contain("Tom 1");
    }

    [Fact]
    public async Task Suggest_MatchesRecipeByTitle()
    {
        var recipeId = Guid.NewGuid();
        var request = Request(
            daysToFill: [WeekStart],
            favorites: [new FavoriteDish { MemberName = "Anna", DishName = "Lasagne verde" }],
            recipes: [new RecipeOption { Id = recipeId, Title = "lasagne VERDE" }]);

        var suggestions = await _service.SuggestAsync(request);

        suggestions[0].RecipeId.Should().Be(recipeId);
    }

    [Fact]
    public async Task Suggest_OnlyFillsRequestedDays()
    {
        var request = Request(
            daysToFill: [WeekStart.AddDays(2)],
            favorites:
            [
                new FavoriteDish { MemberName = "Anna", DishName = "Dish A" },
                new FavoriteDish { MemberName = "Anna", DishName = "Dish B" }
            ]);

        var suggestions = await _service.SuggestAsync(request);

        suggestions.Should().ContainSingle();
        suggestions[0].Date.Should().Be(WeekStart.AddDays(2));
    }
}

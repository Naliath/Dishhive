using Dishhive.Api.Services.Freezy;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>A household member's planning-relevant profile</summary>
public record MemberProfile
{
    public required string Name { get; init; }

    /// <summary>Allergy/intolerance tag names (hard "must not contain")</summary>
    public IReadOnlyList<string> Allergies { get; init; } = [];

    /// <summary>Diet tag names (e.g. vegetarian, no pork)</summary>
    public IReadOnlyList<string> Diets { get; init; } = [];

    public string? PreferenceNotes { get; init; }
}

/// <summary>A member's favorite dish (denormalized name)</summary>
public record FavoriteDish
{
    public required string MemberName { get; init; }
    public required string DishName { get; init; }
}

/// <summary>Dish history aggregate, including eaten/rating feedback</summary>
public record DishHistoryEntry
{
    public required string DishName { get; init; }
    public int TimesPlanned { get; init; }
    public DateOnly LastPlanned { get; init; }
    public int TimesEaten { get; init; }
    public double? AverageRating { get; init; }
}

/// <summary>A recipe from the store that suggestions may link to</summary>
public record RecipeOption
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Category { get; init; }
}

/// <summary>A meal already on the week plan (context for suggestions)</summary>
public record ExistingMeal
{
    public DateOnly Date { get; init; }
    public string? DishName { get; init; }
    public string? VagueInstruction { get; init; }
}

/// <summary>
/// Input for meal suggestions: the planning context a provider may take into account.
/// Assembled by <see cref="MealSuggestionRequestBuilder"/>.
/// </summary>
public record MealSuggestionRequest
{
    public DateOnly WeekStart { get; init; }
    public IReadOnlyList<MemberProfile> Members { get; init; } = [];
    public IReadOnlyList<FavoriteDish> Favorites { get; init; } = [];
    public IReadOnlyList<DishHistoryEntry> RecentDishes { get; init; } = [];
    public IReadOnlyList<RecipeOption> KnownRecipes { get; init; } = [];
    public IReadOnlyList<ExistingMeal> WeekPlan { get; init; } = [];

    /// <summary>
    /// Days the provider should propose a dinner for: days without a dinner main,
    /// or whose dinner is a vague instruction only (the suggestion resolves it).
    /// Suggestions never overwrite concretely planned dishes.
    /// </summary>
    public IReadOnlyList<DateOnly> DaysToFill { get; init; } = [];

    public IReadOnlyList<FrozenItem> AvailableFrozenItems { get; init; } = [];

    /// <summary>
    /// Free-text planning instructions from the user (e.g. "3 days vegetarian").
    /// Interpreted by the LLM provider; the rules fallback ignores it.
    /// </summary>
    public string? Instructions { get; init; }
}

public record MealSuggestion
{
    public DateOnly Date { get; init; }
    public Guid? RecipeId { get; init; }
    public string? DishName { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Meal suggestion provider seam (see docs/features/ai-week-planning.md).
/// The planner depends only on this interface; the registered implementation is
/// LLM-backed when AI is configured (Ai:Provider), with a deterministic rules
/// fallback, and a no-op otherwise.
/// </summary>
public interface IMealSuggestionService
{
    /// <summary>Whether suggestions are available (drives UI visibility)</summary>
    bool IsEnabled { get; }

    Task<IReadOnlyList<MealSuggestion>> SuggestAsync(MealSuggestionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation when AI is not configured: feature reports disabled
/// and yields no suggestions.
/// </summary>
public class NoOpMealSuggestionService : IMealSuggestionService
{
    public bool IsEnabled => false;

    public Task<IReadOnlyList<MealSuggestion>> SuggestAsync(MealSuggestionRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MealSuggestion>>([]);
    }
}

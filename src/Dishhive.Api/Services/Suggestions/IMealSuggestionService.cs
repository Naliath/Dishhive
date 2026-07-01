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

/// <summary>
/// Ingredient names of a recipe, for the post-hoc allergy check only. Never written
/// into the prompt (it would bloat context); populated for the ranked candidate
/// recipes by <see cref="MealSuggestionRequestBuilder"/>. Only ingredient names are
/// used — recipe organization tags would false-positive (e.g. "nut-free" → "nut").
/// </summary>
public record RecipeAllergenInfo
{
    public IReadOnlyList<string> Ingredients { get; init; } = [];
}

/// <summary>
/// A #[Collection Name] reference resolved to its member recipe titles. Dates list
/// the days whose instruction referenced the collection (a dish for such a day must
/// come from the titles); an empty list means the global instructions referenced it
/// (its recipes should be preferred). Titles are least-recently-planned first and
/// capped, see <see cref="CollectionMentionResolver"/>.
/// </summary>
public record CollectionConstraint
{
    public required string Name { get; init; }
    public IReadOnlyList<string> RecipeTitles { get; init; } = [];
    public IReadOnlyList<DateOnly> Dates { get; init; } = [];
}

/// <summary>
/// An @[Source] reference resolved to a website host, so the LLM's search tool can
/// restrict to that site (e.g. "find something from @[Dagelijkse Kost]"). Dates list
/// the days whose instruction referenced the source; an empty list means the global
/// instructions referenced it. See <see cref="SourceMentionResolver"/>.
/// </summary>
public record SourceConstraint
{
    public required string Name { get; init; }
    public required string Host { get; init; }
    public IReadOnlyList<DateOnly> Dates { get; init; } = [];
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

    /// <summary>
    /// Resolved #[Collection Name] references from day instructions and the global
    /// instructions text (see <see cref="CollectionMentionResolver"/>)
    /// </summary>
    public IReadOnlyList<CollectionConstraint> CollectionConstraints { get; init; } = [];

    /// <summary>
    /// Resolved @[Source] references (external recipe sites) from the day and global
    /// instructions (see <see cref="SourceMentionResolver"/>). Empty when web search
    /// is unconfigured or nothing referenced a source.
    /// </summary>
    public IReadOnlyList<SourceConstraint> SourceConstraints { get; init; } = [];

    /// <summary>
    /// Allergen data (ingredients + tags) per known recipe id, for the post-hoc
    /// allergy check only — not prompted. Populated for the ranked candidates.
    /// </summary>
    public IReadOnlyDictionary<Guid, RecipeAllergenInfo> RecipeAllergens { get; init; }
        = new Dictionary<Guid, RecipeAllergenInfo>();
}

/// <summary>Which provider produced a suggestion; drives a small UI marker</summary>
public enum MealSuggestionSource
{
    Ai,
    RulesFallback
}

public record MealSuggestion
{
    public DateOnly Date { get; init; }
    public Guid? RecipeId { get; init; }
    public string? DishName { get; init; }
    public string? Reason { get; init; }

    /// <summary>Whether this came from the LLM or the deterministic fallback</summary>
    public MealSuggestionSource Source { get; init; } = MealSuggestionSource.Ai;

    /// <summary>
    /// Set when the post-hoc allergy check found the linked recipe conflicts with a
    /// household allergy. Heuristic (ingredient-name substring match) so it only
    /// warns — the suggestion is kept, never silently dropped.
    /// </summary>
    public string? AllergyWarning { get; init; }

    /// <summary>Freezy item id when this dish comes from the freezer; reserves stock once accepted</summary>
    public string? FreezyItemRef { get; init; }

    /// <summary>Units of the freezer item this dish reserves (≥1 when FreezyItemRef is set)</summary>
    public int FreezyItemQuantity { get; init; }

    /// <summary>
    /// URL of an external recipe the model found (not yet in the store). Accepting the
    /// suggestion imports it from here before planning it. Null for known-recipe/plain picks.
    /// </summary>
    public string? SourceUrl { get; init; }

    /// <summary>Friendly source name for an external suggestion (e.g. "Dagelijkse Kost" or the host)</summary>
    public string? SourceName { get; init; }
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

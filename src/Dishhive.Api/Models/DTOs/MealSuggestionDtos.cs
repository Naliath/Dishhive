using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.DTOs;

public class SuggestWeekRequestDto
{
    [Required]
    public DateOnly WeekStart { get; set; }

    /// <summary>Empty = all active household members (guests excluded)</summary>
    public List<Guid> AttendeeIds { get; set; } = new();

    /// <summary>
    /// Optional free-text planning instructions for the LLM (e.g. "3 days vegetarian,
    /// at least one fish dish"). Ignored by the deterministic rules fallback.
    /// </summary>
    [MaxLength(500)]
    public string? Instructions { get; set; }
}

public class MealSuggestionDto
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public Guid? RecipeId { get; set; }

    /// <summary>Title of the matched recipe, when the suggestion links to one</summary>
    public string? RecipeTitle { get; set; }

    public string DishName { get; set; } = string.Empty;
    public string? Reason { get; set; }

    /// <summary>True when this day was filled by the deterministic fallback, not the LLM</summary>
    public bool FromFallback { get; set; }

    /// <summary>
    /// Warning that the linked recipe conflicts with a household allergy (exact
    /// facts match on assessed recipes, ingredient-substring heuristic otherwise);
    /// surfaced in the review dialog (never used to hide the suggestion)
    /// </summary>
    public string? AllergyWarning { get; set; }

    /// <summary>
    /// Warning that the linked recipe's assessed facts conflict with an attendee's
    /// diet tag (e.g. a meat dish while a vegetarian attends); softer than an
    /// allergy warning and likewise never hides the suggestion
    /// </summary>
    public string? DietWarning { get; set; }

    /// <summary>Freezy item id when this dish comes from the freezer (reserves stock once accepted)</summary>
    public string? FreezyItemRef { get; set; }

    /// <summary>Units of the freezer item this dish reserves</summary>
    public int FreezyItemQuantity { get; set; }

    /// <summary>
    /// URL of an external recipe the AI found; accepting the suggestion imports it from
    /// here before planning it. Null for known-recipe/plain suggestions.
    /// </summary>
    public string? SourceUrl { get; set; }

    /// <summary>Friendly source name for an external suggestion (e.g. "Dagelijkse Kost")</summary>
    public string? SourceName { get; set; }
}

/// <summary>
/// Suggestions response; enabled=false with an empty list when AI is not
/// configured (Freezy pattern: no error, the UI hides the feature)
/// </summary>
public class MealSuggestionsDto
{
    public Guid BatchId { get; set; }
    public bool Enabled { get; set; }
    public List<MealSuggestionDto> Suggestions { get; set; } = new();

    /// <summary>
    /// How many known recipes were kept out of planning because their assessed
    /// facts conflict with an attendee's allergy exclusions. Surfaced in the
    /// review dialog so a false-positive AI fact is discoverable instead of a
    /// recipe silently never appearing again.
    /// </summary>
    public int ExcludedForAllergies { get; set; }
}

public class AcceptMealSuggestionsRequestDto
{
    public Guid BatchId { get; set; }

    [MinLength(1)]
    public List<AcceptMealSuggestionDto> Suggestions { get; set; } = new();
}

public class AcceptMealSuggestionDto
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public Guid? RecipeId { get; set; }

    [Required, MaxLength(200)]
    public string DishName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? FreezyItemRef { get; set; }

    public int FreezyItemQuantity { get; set; }

    [MaxLength(1000), Url]
    public string? SourceUrl { get; set; }
}

public class AcceptMealSuggestionsResponseDto
{
    public List<AcceptMealSuggestionResultDto> Results { get; set; } = new();
}

public class AcceptMealSuggestionResultDto
{
    public Guid SuggestionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DishName { get; set; } = string.Empty;
    public Guid? PlannedMealId { get; set; }
    public Guid? RecipeId { get; set; }
    public string? Error { get; set; }
}

public class SuggestionStatusDto
{
    public bool Enabled { get; set; }
}

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
    public DateOnly Date { get; set; }
    public Guid? RecipeId { get; set; }

    /// <summary>Title of the matched recipe, when the suggestion links to one</summary>
    public string? RecipeTitle { get; set; }

    public string DishName { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

/// <summary>
/// Suggestions response; enabled=false with an empty list when AI is not
/// configured (Freezy pattern: no error, the UI hides the feature)
/// </summary>
public class MealSuggestionsDto
{
    public bool Enabled { get; set; }
    public List<MealSuggestionDto> Suggestions { get; set; } = new();
}

public class SuggestionStatusDto
{
    public bool Enabled { get; set; }
}

namespace Dishhive.Api.Models;

/// <summary>
/// A family member's 1–5 star rating of a planned meal ("loved" = 4 or higher).
/// One rating per (meal, member); re-rating overwrites. Feeds statistics and
/// AI meal suggestions (see docs/features/past-dishes-and-statistics.md).
/// </summary>
public class MealRating
{
    public Guid PlannedMealId { get; set; }

    public PlannedMeal? PlannedMeal { get; set; }

    public Guid FamilyMemberId { get; set; }

    public FamilyMember? FamilyMember { get; set; }

    /// <summary>1 (disliked) to 5 (loved); validated in the API</summary>
    public int Rating { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

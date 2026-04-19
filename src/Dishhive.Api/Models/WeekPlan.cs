using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// A week meal plan, anchored to the Monday of that week.
/// </summary>
public class WeekPlan
{
    public Guid Id { get; set; }

    /// <summary>The Monday of this week (used as the unique key for a week).</summary>
    public DateOnly WeekStartDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PlannedMeal> Meals { get; set; } = [];
}

/// <summary>
/// A planned meal for a specific day and meal type within a week plan.
/// </summary>
public class PlannedMeal
{
    public Guid Id { get; set; }

    public Guid WeekPlanId { get; set; }
    public WeekPlan WeekPlan { get; set; } = null!;

    public DayOfWeek DayOfWeek { get; set; }

    public MealType MealType { get; set; } = MealType.Dinner;

    /// <summary>Link to a stored recipe, if a specific recipe is planned.</summary>
    public Guid? RecipeId { get; set; }

    /// <summary>Free-text vague instruction (e.g., "something with fish", "leftovers").</summary>
    [MaxLength(500)]
    public string? VagueInstruction { get; set; }

    /// <summary>When true, this meal uses a frozen item from Freezy.</summary>
    public bool IsFromFreezer { get; set; } = false;

    /// <summary>External reference to a Freezy FreezerItem ID (not a FK — different database).</summary>
    public Guid? FreezerItemId { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>
    /// IDs of FamilyMembers attending this meal.
    /// Stored as JSON array column in PostgreSQL.
    /// </summary>
    public List<Guid> AttendeeIds { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum MealType
{
    Breakfast,
    Lunch,
    Dinner,
    Snack
}

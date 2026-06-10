using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

public enum MealType
{
    Breakfast = 0,
    Lunch = 1,
    Dinner = 2,
    Snack = 3
}

/// <summary>
/// A meal slot on the week plan. Exactly one slot per date + meal type.
/// At least one of RecipeId, DishName or VagueInstruction must be set.
/// Past rows double as the dish history (see docs/features/past-dishes-and-statistics.md).
/// </summary>
public class PlannedMeal
{
    public Guid Id { get; set; }

    public DateOnly Date { get; set; }

    public MealType MealType { get; set; } = MealType.Dinner;

    /// <summary>
    /// Linked recipe, when planning style is "recipe"
    /// </summary>
    public Guid? RecipeId { get; set; }

    public Recipe? Recipe { get; set; }

    /// <summary>
    /// Dish name; always denormalized from the recipe title when a recipe is linked,
    /// so history survives recipe deletion or rename
    /// </summary>
    [MaxLength(200)]
    public string? DishName { get; set; }

    /// <summary>
    /// Vague planning intention such as "something with fish" or "leftovers"
    /// </summary>
    [MaxLength(500)]
    public string? VagueInstruction { get; set; }

    /// <summary>
    /// Opaque reference to a Freezy item when the meal comes from the freezer
    /// </summary>
    [MaxLength(100)]
    public string? FreezyItemRef { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<PlannedMealAttendee> Attendees { get; set; } = new();
}

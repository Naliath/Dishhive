using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.DTOs;

public class PlannedMealDto
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public MealType MealType { get; set; }
    public Course Course { get; set; }
    public Guid? RecipeId { get; set; }

    /// <summary>Convenience for the UI; null when the linked recipe was deleted</summary>
    public string? RecipeTitle { get; set; }

    public string? DishName { get; set; }
    public string? VagueInstruction { get; set; }
    public string? FreezyItemRef { get; set; }
    public string? Notes { get; set; }
    public EatenStatus? Eaten { get; set; }
    public List<Guid> AttendeeIds { get; set; } = new();
    public List<MealRatingDto> Ratings { get; set; } = new();
}

public class MealRatingDto
{
    public Guid FamilyMemberId { get; set; }
    public int Rating { get; set; }
}

/// <summary>Marks a meal as eaten or skipped; null clears the mark</summary>
public class SetEatenDto
{
    public EatenStatus? Status { get; set; }
}

public class SetRatingDto
{
    [Range(1, 5)]
    public int Rating { get; set; }
}

/// <summary>Attaches a recipe to a planned meal (e.g. from the shopping list's "still to decide")</summary>
public class SetMealRecipeDto
{
    [Required]
    public Guid RecipeId { get; set; }
}

public class CreatePlannedMealDto
{
    [Required]
    public DateOnly Date { get; set; }

    public MealType MealType { get; set; } = MealType.Dinner;

    public Course Course { get; set; } = Course.Main;

    public Guid? RecipeId { get; set; }

    [MaxLength(200)]
    public string? DishName { get; set; }

    [MaxLength(500)]
    public string? VagueInstruction { get; set; }

    [MaxLength(100)]
    public string? FreezyItemRef { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public List<Guid> FamilyMemberIds { get; set; } = new();
}

/// <summary>Update replaces all fields including the attendee list</summary>
public class UpdatePlannedMealDto : CreatePlannedMealDto
{
}

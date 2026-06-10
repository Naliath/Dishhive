using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.DTOs;

public class PlannedMealDto
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public MealType MealType { get; set; }
    public Guid? RecipeId { get; set; }

    /// <summary>Convenience for the UI; null when the linked recipe was deleted</summary>
    public string? RecipeTitle { get; set; }

    public string? DishName { get; set; }
    public string? VagueInstruction { get; set; }
    public string? FreezyItemRef { get; set; }
    public string? Notes { get; set; }
    public List<Guid> AttendeeIds { get; set; } = new();
}

public class CreatePlannedMealDto
{
    [Required]
    public DateOnly Date { get; set; }

    public MealType MealType { get; set; } = MealType.Dinner;

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

using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.History;

public class DishHistoryEntry
{
    public Guid Id { get; set; }

    public DateOnly Date { get; set; }

    public Models.Planning.MealType MealType { get; set; }

    public Guid? RecipeId { get; set; }

    /// <summary>Display label captured at planning time so deletes/renames don't lose history.</summary>
    [Required]
    [MaxLength(300)]
    public string DishLabel { get; set; } = string.Empty;

    public Guid? PlannedSlotId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class DishFavorite
{
    public Guid Id { get; set; }

    public Guid FamilyMemberId { get; set; }

    public Guid? RecipeId { get; set; }

    [Required]
    [MaxLength(300)]
    public string DishLabel { get; set; } = string.Empty;
}

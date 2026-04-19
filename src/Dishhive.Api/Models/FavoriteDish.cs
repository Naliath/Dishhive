using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// A dish marked as a favorite by a family member.
/// </summary>
public class FavoriteDish
{
    public Guid Id { get; set; }

    public Guid FamilyMemberId { get; set; }
    public FamilyMember FamilyMember { get; set; } = null!;

    /// <summary>Optional link to a stored Recipe.</summary>
    public Guid? RecipeId { get; set; }

    /// <summary>
    /// Free-text name for dishes that don't have a stored Recipe entry.
    /// When RecipeId is set, this mirrors the recipe title for convenience.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string DishName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// A favorite dish of a family member: either a link to a known recipe,
/// a free-text dish name, or both (at least one is required, validated in the API)
/// </summary>
public class FamilyMemberFavorite
{
    public Guid Id { get; set; }

    public Guid FamilyMemberId { get; set; }

    public FamilyMember? FamilyMember { get; set; }

    /// <summary>Favorite known recipe; cleared (set null) when the recipe is deleted</summary>
    public Guid? RecipeId { get; set; }

    public Recipe? Recipe { get; set; }

    /// <summary>Free-text favorite dish; also denormalized from the recipe title when linked</summary>
    [MaxLength(200)]
    public string? DishName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

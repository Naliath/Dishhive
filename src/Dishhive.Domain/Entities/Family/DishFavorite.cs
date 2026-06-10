namespace Dishhive.Domain.Entities.Family;

using Dishhive.Domain.Common;

/// <summary>
/// Represents a family member's favorite dish.
/// </summary>
public class DishFavorite : BaseEntity
{
    public Guid FamilyMemberId { get; set; }
    public Guid RecipeId { get; set; }
    public int Rating { get; set; } = 5;
}

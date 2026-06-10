
namespace Dishhive.Domain.Entities.Recipes;

using Dishhive.Domain.Common;

/// <summary>
/// Represents a single ingredient in a recipe.
/// Stores normalized (metric) values and preserves original source values.
/// </summary>
public class Ingredient : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public double? NormalizedQuantity { get; set; }
    public string? NormalizedUnit { get; set; }
    public string? OriginalValue { get; set; }
    public string? OriginalUnit { get; set; }
    public string? Notes { get; set; }
    public int DisplayOrder { get; set; }
    public Guid RecipeId { get; set; }
}

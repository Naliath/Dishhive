namespace Dishhive.Domain.Entities.Recipes;

using Dishhive.Domain.Common;

/// <summary>
/// A single preparation step in a recipe.
/// </summary>
public class PreparationStep : BaseEntity
{
    public int Order { get; set; }
    public string Instruction { get; set; } = string.Empty;
    public Guid RecipeId { get; set; }
}
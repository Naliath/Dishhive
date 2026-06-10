
namespace Dishhive.Domain.Entities.Recipes;

using Dishhive.Domain.Common;

/// <summary>
/// A tag that can be applied to recipes for categorization and filtering.
/// </summary>
public class Tag : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid RecipeId { get; set; }
}

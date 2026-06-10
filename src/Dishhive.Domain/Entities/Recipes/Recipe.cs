
namespace Dishhive.Domain.Entities.Recipes;

using Dishhive.Domain.Common;

/// <summary>
/// Core recipe entity. A recipe defines how to prepare a dish.
/// </summary>
public class Recipe : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Servings { get; set; } = 1;
    public int? PreparationMinutes { get; set; }
    public int? CookingMinutes { get; set; }
    public string? Category { get; set; }
    public string? PictureUrl { get; set; }
    public string? VideoUrl { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourceName { get; set; }
    public string? SourceProvider { get; set; }
    public string? SourceData { get; set; }

    // Navigation
    public List<Ingredient> Ingredients { get; set; } = new List<Ingredient>();
    public List<PreparationStep> PreparationSteps { get; set; } = new List<PreparationStep>();
    public List<Tag> Tags { get; set; } = new List<Tag>();
}

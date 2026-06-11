using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// A reusable recipe tag for organizing the library (e.g. "pasta", "quick",
/// "comfort food"). Same implicit lifecycle as dietary tags: created when first
/// assigned to a recipe, removed when no recipe uses them — the pool always
/// reflects real usage. See docs/features/recipe-organization.md.
/// </summary>
public class RecipeTag
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<RecipeTagAssignment> Recipes { get; set; } = [];
}

/// <summary>Join table linking recipes to their tags</summary>
public class RecipeTagAssignment
{
    public Guid RecipeId { get; set; }

    public Recipe? Recipe { get; set; }

    public Guid RecipeTagId { get; set; }

    public RecipeTag? RecipeTag { get; set; }
}

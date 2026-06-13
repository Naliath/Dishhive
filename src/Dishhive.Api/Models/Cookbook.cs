using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// A collection: a named set of explicitly curated recipes. A recipe can be in any
/// number of collections. Collections replace the earlier saved-filter cookbooks
/// (see docs/features/recipe-organization.md); dynamic slicing stays with tags and
/// search. Names may not contain square brackets — they delimit the #[Name] mention
/// syntax in planning instruction fields (see docs/features/ai-week-planning.md).
/// </summary>
public class Cookbook
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public List<CookbookEntry> Entries { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Explicit membership of a recipe in a collection</summary>
public class CookbookEntry
{
    public Guid CookbookId { get; set; }

    public Cookbook? Cookbook { get; set; }

    public Guid RecipeId { get; set; }

    public Recipe? Recipe { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

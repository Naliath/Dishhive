using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// A recipe in the Dishhive recipe store.
/// </summary>
public class Recipe
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public int Servings { get; set; } = 4;

    public int? PrepTimeMinutes { get; set; }

    public int? CookTimeMinutes { get; set; }

    /// <summary>
    /// Stores the recipe picture as a Base64-encoded JPEG data URI
    /// (e.g. "data:image/jpeg;base64,…"). Saved locally on import so the image
    /// is always available regardless of the original CDN.
    /// </summary>
    public string? PictureUrl { get; set; }

    [MaxLength(2000)]
    public string? VideoUrl { get; set; }

    /// <summary>URL of the original source (e.g., Dagelijkse Kost page).</summary>
    [MaxLength(2000)]
    public string? SourceUrl { get; set; }

    /// <summary>Human-readable source name (e.g., "DagelijkseKost").</summary>
    [MaxLength(100)]
    public string? SourceName { get; set; }

    /// <summary>Raw JSON-LD or other structured data from the import source, for traceability.</summary>
    public string? SourceRawData { get; set; }

    /// <summary>Tags stored as a JSON array column (e.g., ["vegetarian", "quick"]).</summary>
    public List<string> Tags { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RecipeIngredient> Ingredients { get; set; } = [];
    public ICollection<RecipeStep> Steps { get; set; } = [];
}

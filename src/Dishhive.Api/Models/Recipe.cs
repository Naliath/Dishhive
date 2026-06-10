using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// A recipe in the household recipe store, entered manually or imported from a source
/// </summary>
public class Recipe
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>
    /// Intended number of people/servings
    /// </summary>
    [Range(1, 100)]
    public int Servings { get; set; } = 4;

    public int? PrepTimeMinutes { get; set; }

    public int? CookTimeMinutes { get; set; }

    public int? TotalTimeMinutes { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>
    /// Comma-separated keywords (e.g. "Zoet, Zomer, Dessert")
    /// </summary>
    [MaxLength(500)]
    public string? Keywords { get; set; }

    /// <summary>
    /// Original image URL at the source; kept for traceability even when the image
    /// is stored locally in <see cref="ImageData"/>
    /// </summary>
    [MaxLength(1000)]
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Locally stored image bytes, downloaded at import time so recipes don't depend on
    /// expiring source URLs. Served via GET /api/recipes/{id}/image.
    /// </summary>
    public byte[]? ImageData { get; set; }

    /// <summary>
    /// MIME type of <see cref="ImageData"/> (e.g. "image/jpeg")
    /// </summary>
    [MaxLength(100)]
    public string? ImageContentType { get; set; }

    [MaxLength(1000)]
    public string? VideoUrl { get; set; }

    /// <summary>
    /// Canonical URL of the source page; null for manually entered recipes
    /// </summary>
    [MaxLength(1000)]
    public string? SourceUrl { get; set; }

    /// <summary>
    /// Key of the import provider that produced this recipe (e.g. "dagelijkse-kost")
    /// </summary>
    [MaxLength(100)]
    public string? SourceProvider { get; set; }

    /// <summary>
    /// Raw extracted source payload (JSON) kept for traceability and re-parsing
    /// </summary>
    public string? SourceRawData { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<RecipeIngredient> Ingredients { get; set; } = new();

    public List<RecipeStep> Steps { get; set; } = new();
}

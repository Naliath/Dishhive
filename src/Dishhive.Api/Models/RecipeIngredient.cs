using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// An ingredient in a recipe, with both normalized (metric) and original (source) values.
/// </summary>
public class RecipeIngredient
{
    public Guid Id { get; set; }

    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Normalized quantity in metric units.</summary>
    public decimal? Quantity { get; set; }

    /// <summary>Normalized unit (metric). E.g., "g", "ml", "tbsp".</summary>
    [MaxLength(50)]
    public string? Unit { get; set; }

    /// <summary>Original quantity from import source (before any conversion).</summary>
    public decimal? OriginalQuantity { get; set; }

    /// <summary>Original unit from import source.</summary>
    [MaxLength(50)]
    public string? OriginalUnit { get; set; }

    /// <summary>Additional notes (e.g., "finely chopped", "at room temperature").</summary>
    [MaxLength(500)]
    public string? Notes { get; set; }

    public int SortOrder { get; set; }
}

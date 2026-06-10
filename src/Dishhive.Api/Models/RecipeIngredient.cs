using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// An ingredient line of a recipe. Stores normalized (metric) values used by Dishhive
/// alongside the verbatim original source values so manual correction stays possible.
/// See docs/features/measurement-preferences.md for the model rules.
/// </summary>
public class RecipeIngredient
{
    public Guid Id { get; set; }

    public Guid RecipeId { get; set; }

    public Recipe? Recipe { get; set; }

    public int SortOrder { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Normalized quantity (metric canonical units); null when the line is unparseable
    /// </summary>
    public decimal? Quantity { get; set; }

    /// <summary>
    /// Normalized unit ("g", "kg", "ml", "l", "piece", or culinary pass-through like "el")
    /// </summary>
    [MaxLength(50)]
    public string? Unit { get; set; }

    /// <summary>
    /// Verbatim source line (e.g. "200 gram suiker"); never modified by conversion
    /// </summary>
    [Required]
    [MaxLength(300)]
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// Parsed source quantity before any conversion
    /// </summary>
    public decimal? OriginalQuantity { get; set; }

    /// <summary>
    /// Source unit before any conversion
    /// </summary>
    [MaxLength(50)]
    public string? OriginalUnit { get; set; }
}

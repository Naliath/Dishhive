namespace Dishhive.Api.Models.DTOs;

/// <summary>
/// Represents a recipe imported from an external source, before being persisted.
/// </summary>
public record ImportedRecipeDto(
    string Title,
    string? Description,
    List<ImportedIngredientDto> Ingredients,
    List<string> Steps,
    int? Servings,
    string? PictureUrl,
    string? VideoUrl,
    string SourceUrl,
    string SourceName,
    string? SourceRawData
);

/// <summary>
/// An ingredient extracted from an import source.
/// </summary>
public record ImportedIngredientDto(
    /// <summary>The raw text as it appeared in the source (e.g., "250 g bloem").</summary>
    string RawText,
    /// <summary>Parsed ingredient name (Phase 1: same as RawText).</summary>
    string Name,
    /// <summary>Parsed quantity from source.</summary>
    decimal? OriginalQuantity,
    /// <summary>Parsed unit from source.</summary>
    string? OriginalUnit
);

/// <summary>
/// A frozen item from Freezy, mapped to Dishhive's representation.
/// </summary>
public record FrozenItemDto(
    Guid Id,
    string Name,
    int Quantity,
    string Unit,
    DateTime? ExpirationDate
);

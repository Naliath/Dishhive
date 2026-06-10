namespace Dishhive.Api.Services.Import;

/// <summary>
/// Provider-agnostic result of extracting a recipe from an external source.
/// See docs/features/recipe-import.md for the field mapping per source.
/// </summary>
public record ImportedRecipe
{
    public required string Title { get; init; }

    public string? Description { get; init; }

    /// <summary>Verbatim ingredient lines as published by the source</summary>
    public IReadOnlyList<string> IngredientLines { get; init; } = [];

    /// <summary>Ordered preparation step texts</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    /// <summary>Intended number of servings/people</summary>
    public int? Servings { get; init; }

    public string? ImageUrl { get; init; }

    public string? VideoUrl { get; init; }

    /// <summary>Canonical URL of the source page</summary>
    public string? SourceUrl { get; init; }

    public int? PrepTimeMinutes { get; init; }

    public int? CookTimeMinutes { get; init; }

    public int? TotalTimeMinutes { get; init; }

    public string? Category { get; init; }

    /// <summary>Comma-separated keywords</summary>
    public string? Keywords { get; init; }

    /// <summary>Raw source payload (JSON) for traceability and re-parsing</summary>
    public string? RawData { get; init; }
}

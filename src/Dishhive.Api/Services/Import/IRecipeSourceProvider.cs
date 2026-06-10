namespace Dishhive.Api.Services.Import;

/// <summary>
/// A pluggable recipe import source. Providers are pure extractors (HTML in,
/// <see cref="ImportedRecipe"/> out); HTTP fetching lives in <see cref="RecipeImportService"/>
/// so providers stay unit-testable against stored fixtures.
/// Adding a source = one provider class + DI registration + a fixture test.
/// </summary>
public interface IRecipeSourceProvider
{
    /// <summary>Stable provider key stored on imported recipes (e.g. "dagelijkse-kost")</summary>
    string Key { get; }

    /// <summary>Whether this provider can extract recipes from the given URL</summary>
    bool CanHandle(Uri url);

    /// <summary>Extracts a recipe from the fetched page content</summary>
    /// <exception cref="RecipeExtractionFailedException">No recipe data found in the page</exception>
    Task<ImportedRecipe> ExtractAsync(string html, Uri sourceUrl, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when no registered provider can handle an import URL</summary>
public class UnsupportedRecipeSourceException(string url)
    : Exception($"No recipe import provider is registered for '{url}'.");

/// <summary>Thrown when a page was fetched but contained no extractable recipe data</summary>
public class RecipeExtractionFailedException(string message) : Exception(message);

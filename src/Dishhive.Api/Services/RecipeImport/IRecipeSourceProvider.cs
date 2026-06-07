namespace Dishhive.Api.Services.RecipeImport;

/// <summary>
/// Source-agnostic shape returned by recipe-import providers. Persistence is the
/// caller's job — providers only fetch and parse.
/// </summary>
public sealed record ImportedRecipe(
    string Title,
    string? Description,
    int Servings,
    string? ImageUrl,
    string? VideoUrl,
    Uri SourceUrl,
    string ProviderKey,
    string SourceRawPayload,
    IReadOnlyList<ImportedIngredient> Ingredients,
    IReadOnlyList<ImportedStep> Steps,
    IReadOnlyList<string> Tags);

public sealed record ImportedIngredient(
    int Order,
    string Name,
    decimal? Quantity,
    string? Unit,
    decimal? OriginalQuantity,
    string? OriginalUnit,
    string? Section,
    string? Note);

public sealed record ImportedStep(int Order, string Text);

/// <summary>
/// Plug-in contract for one external recipe source. Implementations are kept side-effect free
/// outside HTTP fetching and HTML/JSON parsing so they're trivially unit-testable from a fixture.
/// </summary>
public interface IRecipeSourceProvider
{
    /// <summary>Stable, lowercase key (e.g. <c>"dagelijksekost"</c>) persisted on the recipe.</summary>
    string ProviderKey { get; }

    bool CanHandle(Uri url);

    Task<ImportedRecipe> FetchAsync(Uri url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parse already-fetched HTML. Exposed separately so tests can run against a captured fixture
    /// without any network access.
    /// </summary>
    ImportedRecipe Parse(Uri sourceUrl, string html);
}

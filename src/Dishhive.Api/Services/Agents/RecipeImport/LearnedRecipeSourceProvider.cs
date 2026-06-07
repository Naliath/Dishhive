using Dishhive.Api.Models.Agents;
using Dishhive.Api.Services.RecipeImport;

namespace Dishhive.Api.Services.Agents.RecipeImport;

/// <summary>
/// A single <see cref="IRecipeSourceProvider"/> that handles every host with a stored
/// <see cref="LearnedRecipeSource"/>. Delegates parsing to <see cref="JsonLdRecipeParser"/>
/// or <see cref="XPathRecipeParser"/> depending on the blueprint strategy.
///
/// This is the "static importer next time" — once the recipe-import agent has learned
/// a source, every subsequent visit is handled by this provider with no LLM call.
/// </summary>
public sealed class LearnedRecipeSourceProvider : IRecipeSourceProvider
{
    private readonly ILearnedSourceStore _store;
    private readonly HttpClient _http;
    private readonly ILogger<LearnedRecipeSourceProvider> _logger;

    public LearnedRecipeSourceProvider(
        ILearnedSourceStore store,
        HttpClient http,
        ILogger<LearnedRecipeSourceProvider> logger)
    {
        _store = store;
        _http = http;
        _logger = logger;
    }

    public string ProviderKey => "learned";

    public bool CanHandle(Uri url)
    {
        if (url.Scheme is not ("http" or "https")) return false;
        // Synchronous existence check — cheap on a tiny table; EF translates to a single SELECT.
        var host = url.Host.ToLowerInvariant();
        return _store.FindByHostAsync(host).GetAwaiter().GetResult() is not null;
    }

    public async Task<ImportedRecipe> FetchAsync(Uri url, CancellationToken cancellationToken = default)
    {
        var row = await _store.FindByHostAsync(url.Host.ToLowerInvariant(), cancellationToken)
            ?? throw new InvalidOperationException($"No learned blueprint for host '{url.Host}'.");

        using var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        var imported = ParseInternal(url, html, row);
        await _store.RecordUseAsync(row.Id, cancellationToken);
        return imported;
    }

    public ImportedRecipe Parse(Uri sourceUrl, string html)
    {
        var row = _store.FindByHostAsync(sourceUrl.Host.ToLowerInvariant()).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException($"No learned blueprint for host '{sourceUrl.Host}'.");
        return ParseInternal(sourceUrl, html, row);
    }

    private ImportedRecipe ParseInternal(Uri sourceUrl, string html, LearnedRecipeSource row)
    {
        var blueprint = LearnedSourceStore.Deserialize(row.BlueprintJson);

        ImportedRecipe? parsed = blueprint.Strategy switch
        {
            LearnedRecipeSourceStrategy.JsonLd => JsonLdRecipeParser.TryParse(sourceUrl, html, row.ProviderKey),
            LearnedRecipeSourceStrategy.XPath => XPathRecipeParser.TryParse(sourceUrl, html, row.ProviderKey, blueprint),
            _ => null,
        };

        if (parsed is null)
        {
            _logger.LogWarning("Learned blueprint for {Host} ({Strategy}) failed to parse {Url}.",
                row.Host, row.Strategy, sourceUrl);
            throw new InvalidOperationException(
                $"Learned blueprint for '{row.Host}' could not parse the page. Delete the learned source to re-learn it.");
        }

        return parsed;
    }
}

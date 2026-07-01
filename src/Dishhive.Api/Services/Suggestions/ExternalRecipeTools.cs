using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.WebSearch;
using Microsoft.Extensions.AI;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>
/// The read-only tools the LLM planner may call to discover recipes on the web:
/// <c>search_recipes</c> (app-provided web search, so models without native search can
/// still browse) and <c>get_recipe</c> (fetch + structured extract via the internal
/// scraper, falling back to page text). Neither writes anything — an accepted external
/// suggestion is imported later, on the user's say-so. Built per suggestion call so the
/// tools can carry that request's default source host.
/// </summary>
public class ExternalRecipeTools
{
    private readonly IWebSearchClient _webSearch;
    private readonly IRecipeImportService _importService;
    private readonly int _maxResults;
    private readonly string? _defaultSite;
    private readonly string _requestId;
    private readonly ILogger _logger;

    // Models sometimes re-issue an identical tool call (e.g. get_recipe on the same URL
    // twice in a row) within one suggestion request — wasted round-trips that eat into
    // the shared agent time budget. Memoizing per-request short-circuits repeats instead
    // of re-fetching; keyed Task so concurrent identical calls share the one in-flight
    // fetch rather than racing.
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<SearchHit>>> _searchCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<GetRecipeResult>> _recipeCache = new(StringComparer.OrdinalIgnoreCase);

    public ExternalRecipeTools(
        IWebSearchClient webSearch,
        IRecipeImportService importService,
        int maxResults,
        string? defaultSite,
        string requestId,
        ILogger logger)
    {
        _webSearch = webSearch;
        _importService = importService;
        _maxResults = maxResults;
        _defaultSite = defaultSite;
        _requestId = requestId;
        _logger = logger;
    }

    /// <summary>The AITool list to pass in ChatOptions.Tools</summary>
    public IList<AITool> Build() =>
    [
        AIFunctionFactory.Create(SearchRecipesAsync, name: "search_recipes"),
        AIFunctionFactory.Create(GetRecipeAsync, name: "get_recipe")
    ];

    [Description("Search the web for recipes matching the query. Returns candidate pages "
        + "(title + url). Use get_recipe on a promising result to read its details.")]
    // internal for the memoization tests (AIFunctionFactory.Create works on any accessibility)
    internal Task<IReadOnlyList<SearchHit>> SearchRecipesAsync(
        [Description("Search keywords, e.g. 'vegetarian pasta under 30 minutes'")] string query,
        [Description("Optional website host to restrict the search to, e.g. 'dagelijksekost.vrt.be'")] string? site = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveSite = string.IsNullOrWhiteSpace(site) ? _defaultSite : site.Trim();
        var key = $"{query.Trim()}|{effectiveSite}";
        return _searchCache.GetOrAdd(key, _ => SearchRecipesCoreAsync(query, effectiveSite, cancellationToken));
    }

    private async Task<IReadOnlyList<SearchHit>> SearchRecipesCoreAsync(
        string query, string? effectiveSite, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = await _webSearch.SearchAsync(query, effectiveSite, _maxResults, cancellationToken);
        _logger.LogInformation(
            "[{RequestId}] Tool search_recipes(\"{Query}\", site={Site}) → {Count} results in {ElapsedMs}ms",
            _requestId, query, effectiveSite, results.Count, stopwatch.ElapsedMilliseconds);
        return results.Select(r => new SearchHit(r.Title, r.Url, r.Snippet)).ToList();
    }

    [Description("Fetch a recipe page and return its structured recipe (title, times in "
        + "minutes, ingredients, steps). If the page can't be parsed automatically its text "
        + "is returned so you can read it yourself. Use this to verify a candidate meets the "
        + "constraints before proposing it.")]
    internal Task<GetRecipeResult> GetRecipeAsync(
        [Description("The full URL of the recipe page")] string url,
        CancellationToken cancellationToken = default)
    {
        var key = url.Trim();
        return _recipeCache.GetOrAdd(key, _ => GetRecipeCoreAsync(key, cancellationToken));
    }

    private async Task<GetRecipeResult> GetRecipeCoreAsync(string url, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var preview = await _importService.PreviewAsync(url, cancellationToken);
        _logger.LogInformation(
            "[{RequestId}] Tool get_recipe(\"{Url}\") → scrapable={Scrapable}, error={Error} in {ElapsedMs}ms",
            _requestId, url, preview.Scrapable, preview.Error, stopwatch.ElapsedMilliseconds);

        if (preview.Error != null)
        {
            return new GetRecipeResult(false, null, null, null, null, null, null, null, null, preview.Error);
        }

        var r = preview.Recipe;
        return new GetRecipeResult(
            Scrapable: preview.Scrapable,
            Title: r?.Title,
            TotalTimeMinutes: r?.TotalTimeMinutes,
            PrepTimeMinutes: r?.PrepTimeMinutes,
            CookTimeMinutes: r?.CookTimeMinutes,
            Servings: r?.Servings,
            Ingredients: r?.IngredientLines,
            Instructions: r?.Steps,
            PageText: preview.Text,
            Error: null);
    }

    public record SearchHit(string Title, string Url, string? Snippet);

    public record GetRecipeResult(
        bool Scrapable,
        string? Title,
        int? TotalTimeMinutes,
        int? PrepTimeMinutes,
        int? CookTimeMinutes,
        int? Servings,
        IReadOnlyList<string>? Ingredients,
        IReadOnlyList<string>? Instructions,
        string? PageText,
        string? Error);
}

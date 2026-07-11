using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.WebSearch;
using Microsoft.Extensions.AI;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>
/// Per-suggestion-request external recipe session. Search results receive opaque
/// candidate ids; the model can inspect and select those ids but never supplies a URL
/// that the application trusts. The session owns candidate provenance and tool caches.
/// </summary>
public class ExternalRecipeTools : IExternalRecipeSession
{
    private readonly IWebSearchClient _webSearch;
    private readonly IRecipeImportService _importService;
    private readonly int _maxResults;
    private readonly string? _defaultSite;
    private readonly HashSet<string> _allowedHosts;
    private readonly string _requestId;
    private readonly ILogger _logger;
    private int _candidateSequence;

    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<SearchHit>>> _searchCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<GetRecipeResult>> _recipeCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CandidateState> _candidates =
        new(StringComparer.OrdinalIgnoreCase);

    public ExternalRecipeTools(
        IWebSearchClient webSearch,
        IRecipeImportService importService,
        int maxResults,
        string? defaultSite,
        IReadOnlyCollection<string>? allowedHosts,
        string requestId,
        ILogger logger)
    {
        _webSearch = webSearch;
        _importService = importService;
        _maxResults = maxResults;
        _defaultSite = defaultSite;
        _allowedHosts = (allowedHosts ?? [])
            .Select(NormalizeHost)
            .Where(host => host != null)
            .Select(host => host!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _requestId = requestId;
        _logger = logger;
    }

    public IList<AITool> Build() =>
    [
        AIFunctionFactory.Create(SearchRecipesAsync, name: "search_recipes"),
        AIFunctionFactory.Create(GetRecipeAsync, name: "get_recipe")
    ];

    IList<AITool> IExternalRecipeSession.BuildTools() => Build();

    [Description("Search referenced recipe websites. Returns candidateId + title. "
        + "Use get_recipe(candidateId) before selecting a candidate.")]
    internal Task<IReadOnlyList<SearchHit>> SearchRecipesAsync(
        [Description("Search keywords, e.g. 'vegetarian pasta under 30 minutes'")] string query,
        [Description("Optional referenced website host, e.g. 'dagelijksekost.vrt.be'")] string? site = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveSite = string.IsNullOrWhiteSpace(site) ? _defaultSite : site.Trim();
        var key = $"{query.Trim()}|{effectiveSite}";
        return _searchCache.GetOrAdd(key, _ => SearchRecipesCoreAsync(query, effectiveSite, cancellationToken));
    }

    private async Task<IReadOnlyList<SearchHit>> SearchRecipesCoreAsync(
        string query,
        string? effectiveSite,
        CancellationToken cancellationToken)
    {
        var normalizedSite = NormalizeHost(effectiveSite);
        if (normalizedSite != null && _allowedHosts.Count > 0 && !_allowedHosts.Contains(normalizedSite))
        {
            _logger.LogWarning(
                "[{RequestId}] Tool search_recipes rejected unreferenced site {Site}",
                _requestId, effectiveSite);
            return [];
        }

        var stopwatch = Stopwatch.StartNew();
        var results = await _webSearch.SearchAsync(query, effectiveSite, _maxResults, cancellationToken);
        var hits = new List<SearchHit>();
        foreach (var result in results)
        {
            if (!TryNormalizeAllowedUrl(result.Url, out var url))
            {
                _logger.LogWarning(
                    "[{RequestId}] Search result URL outside referenced source hosts was discarded: {Url}",
                    _requestId, result.Url);
                continue;
            }

            var id = $"c{Interlocked.Increment(ref _candidateSequence)}";
            _candidates[id] = new CandidateState(id, url!, result.Title);
            hits.Add(new SearchHit(id, result.Title, result.Snippet));
        }

        _logger.LogInformation(
            "[{RequestId}] Tool search_recipes(\"{Query}\", site={Site}) → {Count} candidates in {ElapsedMs}ms",
            _requestId, query, effectiveSite, hits.Count, stopwatch.ElapsedMilliseconds);
        return hits;
    }

    [Description("Fetch and inspect a candidate returned by search_recipes. Pass its candidateId, "
        + "then copy that same candidateId into externalCandidateId in the final suggestion.")]
    internal Task<GetRecipeResult> GetRecipeAsync(
        [Description("The candidateId returned by search_recipes, e.g. 'c1'")] string candidateId,
        CancellationToken cancellationToken = default)
    {
        var key = candidateId.Trim();
        return _recipeCache.GetOrAdd(key, _ => GetRecipeCoreAsync(key, cancellationToken));
    }

    private async Task<GetRecipeResult> GetRecipeCoreAsync(
        string candidateId,
        CancellationToken cancellationToken)
    {
        if (!_candidates.TryGetValue(candidateId, out var candidate))
        {
            _logger.LogWarning(
                "[{RequestId}] Tool get_recipe rejected unknown candidate {CandidateId}",
                _requestId, candidateId);
            return FailedRecipe(candidateId, "Unknown candidateId. Use search_recipes first.");
        }

        var stopwatch = Stopwatch.StartNew();
        var preview = await _importService.PreviewAsync(candidate.SearchUrl, cancellationToken);
        _logger.LogInformation(
            "[{RequestId}] Tool get_recipe({CandidateId}) → scrapable={Scrapable}, error={Error} in {ElapsedMs}ms",
            _requestId, candidateId, preview.Scrapable, preview.Error, stopwatch.ElapsedMilliseconds);

        if (preview.Error != null)
        {
            return FailedRecipe(candidateId, preview.Error);
        }

        var recipe = preview.Recipe;
        var canonicalUrl = ResolveSourceUrl(recipe?.SourceUrl, candidate.SearchUrl);
        if (canonicalUrl == null || !TryNormalizeAllowedUrl(canonicalUrl, out canonicalUrl))
        {
            return FailedRecipe(candidateId, "The recipe resolved outside the referenced source hosts.");
        }

        candidate.MarkFetched(canonicalUrl!, recipe, preview.Text);
        return new GetRecipeResult(
            CandidateId: candidateId,
            Scrapable: preview.Scrapable,
            Title: recipe?.Title ?? candidate.SearchTitle,
            TotalTimeMinutes: recipe?.TotalTimeMinutes,
            PrepTimeMinutes: recipe?.PrepTimeMinutes,
            CookTimeMinutes: recipe?.CookTimeMinutes,
            Servings: recipe?.Servings,
            Ingredients: recipe?.IngredientLines,
            Instructions: recipe?.Steps,
            PageText: preview.Text,
            Error: null);
    }

    public bool TryResolveCandidate(string? candidateId, out ExternalRecipeCandidate? candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidateId)
            && _candidates.TryGetValue(candidateId.Trim(), out var state)
            && state.Fetched
            && state.CanonicalUrl != null)
        {
            candidate = new ExternalRecipeCandidate(
                state.Id,
                state.CanonicalUrl,
                state.Recipe?.Title ?? state.SearchTitle,
                state.Recipe?.IngredientLines ?? [],
                state.Recipe?.Steps ?? [],
                state.PageText);
            return true;
        }

        candidate = null;
        return false;
    }

    private bool TryNormalizeAllowedUrl(string? value, out string? normalizedUrl)
    {
        normalizedUrl = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var host = NormalizeHost(uri.Host);
        if (host == null || (_allowedHosts.Count > 0 && !_allowedHosts.Contains(host)))
        {
            return false;
        }

        normalizedUrl = uri.AbsoluteUri;
        return true;
    }

    private static GetRecipeResult FailedRecipe(string candidateId, string error) => new(
        CandidateId: candidateId,
        Scrapable: false,
        Title: null,
        TotalTimeMinutes: null,
        PrepTimeMinutes: null,
        CookTimeMinutes: null,
        Servings: null,
        Ingredients: null,
        Instructions: null,
        PageText: null,
        Error: error);

    private static string? ResolveSourceUrl(string? canonicalUrl, string requestedUrl)
    {
        foreach (var candidate in new[] { canonicalUrl, requestedUrl })
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return uri.AbsoluteUri;
            }
        }

        return null;
    }

    private static string? NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var normalized = host.Trim().ToLowerInvariant();
        return normalized.StartsWith("www.", StringComparison.Ordinal) ? normalized[4..] : normalized;
    }

    private sealed class CandidateState(string id, string searchUrl, string searchTitle)
    {
        public string Id { get; } = id;
        public string SearchUrl { get; } = searchUrl;
        public string SearchTitle { get; } = searchTitle;
        public bool Fetched { get; private set; }
        public string? CanonicalUrl { get; private set; }
        public ImportedRecipe? Recipe { get; private set; }
        public string? PageText { get; private set; }

        public void MarkFetched(string canonicalUrl, ImportedRecipe? recipe, string? pageText)
        {
            CanonicalUrl = canonicalUrl;
            Recipe = recipe;
            PageText = pageText;
            Fetched = true;
        }
    }

    public record SearchHit(string CandidateId, string Title, string? Snippet);

    public record GetRecipeResult(
        string CandidateId,
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

public sealed record ExternalRecipeCandidate(
    string Id,
    string SourceUrl,
    string Title,
    IReadOnlyList<string> Ingredients,
    IReadOnlyList<string> Instructions,
    string? PageText);

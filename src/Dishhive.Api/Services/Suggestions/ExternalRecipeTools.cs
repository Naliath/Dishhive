using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.WebSearch;
using Dishhive.Api.Services.Facts;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Ai;
using System.Collections.Concurrent;
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
    private readonly IRecipeFactsExtractor? _factsExtractor;
    private readonly IRecipeResearchQueryRefiner? _queryRefiner;
    private readonly int _generationReserve;
    private readonly bool _prefetchRemaining;
    private readonly IRecipeDishMatcher? _dishMatcher;
    private int _candidateSequence;
    private int _researchCalls;
    private int _searchCount;
    private int _emptySearchCount;
    private int _searchResultCount;
    private int _resolutionCount;
    private int _resolutionFailureCount;
    private long _searchDurationMs;
    private long _resolutionDurationMs;

    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<SearchHit>>>> _searchCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<GetRecipeResult>>> _recipeCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CandidateState> _candidates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CandidateState> _candidatesByUrl = new(StringComparer.Ordinal);

    public ExternalRecipeTools(
        IWebSearchClient webSearch,
        IRecipeImportService importService,
        int maxResults,
        string? defaultSite,
        IReadOnlyCollection<string>? allowedHosts,
        string requestId,
        ILogger logger,
        IRecipeFactsExtractor? factsExtractor = null,
        IRecipeResearchQueryRefiner? queryRefiner = null,
        int generationReserve = 2,
        bool prefetchRemaining = false,
        IRecipeDishMatcher? dishMatcher = null)
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
        _factsExtractor = factsExtractor;
        _queryRefiner = queryRefiner;
        _generationReserve = Math.Max(1, generationReserve);
        _prefetchRemaining = prefetchRemaining;
        _dishMatcher = dishMatcher;
    }

    Task<IReadOnlyList<GetRecipeResult>> IExternalRecipeSession.ResearchAsync(
        List<RecipeResearchRequest> requests,
        CancellationToken cancellationToken) => ResearchRecipesAsync(requests, cancellationToken);

    internal async Task<IReadOnlyList<GetRecipeResult>> ResearchRecipesAsync(
        List<RecipeResearchRequest> requests,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _researchCalls);
        const int maxRequests = 8;
        // The old 24-page cap included failed previews that used no model call.
        // Permit four more inspections only while the shared attempt budget can
        // still cover final generation. The gate remains the hard attempt limit.
        const int maxResolvedCandidates = 28;
        const int waveSize = 4;
        var work = requests.Where(request => !string.IsNullOrWhiteSpace(request.Query)
            && !string.IsNullOrWhiteSpace(request.Site)).Take(maxRequests).Select(request => new ResearchWork(request)).ToList();
        var completed = new Dictionary<string, GetRecipeResult>(StringComparer.OrdinalIgnoreCase);
        var refinementCalls = 0;
        var matched = work.ToDictionary(state => state.Request.ConstraintId,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        int CountEligible(ResearchWork state) => state.Researched.Count(id => Eligible(completed[id], state.Request));
        while (true)
        {
            var progressed = false;
            // Give every requirement a wave before spending the remaining allowance.
            // Finished clauses release their unused capacity to the others.
            foreach (var state in work.Where(state => CountEligible(state) < state.Target))
            {
                if (_queryRefiner != null && state.RefinementCount < 2 && refinementCalls < 4
                    && state.Researched.Count >= Math.Min(waveSize, state.Target)
                    && completed.Count < maxResolvedCandidates
                    && (AiOperationBudget.Current?.RemainingAttempts ?? int.MaxValue) > _generationReserve + 1
                    && (state.RefinementCount == 0 || (state.Pending.Count == 0 && state.Deferred.Count == 0
                        && state.NextQuery >= state.Queries.Count)))
                {
                    state.RefinementCount++;
                    refinementCalls++;
                    var refinements = await _queryRefiner.RefineAsync(new(
                        state.Request.Query, state.Request.Site, state.Request.Course,
                        state.Request.RequiredClasses ?? [], state.Request.ExcludedClasses ?? [],
                        state.Queries.Take(state.NextQuery).ToList(),
                        state.Researched.SelectMany(id => completed[id].ContainsClasses)
                            .Intersect(state.Request.ExcludedClasses ?? []).Distinct().Order().ToList(),
                        CountEligible(state), state.Target,
                        state.Researched.Select(id => completed[id]).Where(result => result.FactsAssessed)
                            .Where(result => Eligible(result, state.Request)).Take(3)
                            .Concat(state.Researched.Select(id => completed[id]).Where(result => result.FactsAssessed
                                && !Eligible(result, state.Request)).Take(3))
                            .Select(result => new RecipeResearchExample(
                                (result.Title ?? "")[..Math.Min(160, result.Title?.Length ?? 0)],
                                result.ContainsClasses, Eligible(result, state.Request))).ToList(),
                        state.Request.FoodRequest), _requestId, cancellationToken);
                    if (refinements.Count > 0)
                    {
                        // Correct retrieval before spending the remaining inspection
                        // budget on more pages from the ineffective first query.
                        while (state.Pending.TryDequeue(out var deferred)) state.Deferred.Enqueue(deferred);
                        state.Queries.RemoveRange(state.NextQuery, state.Queries.Count - state.NextQuery);
                        state.Queries.AddRange(refinements.Take(3));
                    }
                }
                var wave = new List<string>();
                var newCandidates = 0;
                // Slow reasoning calls should not become a serial tail when only
                // one candidate is missing. Once other requirements are satisfied,
                // use a full bounded wave; the call/page limits and generation
                // reserve still apply. These are candidate options, not extra meals.
                var wanted = _prefetchRemaining && work.Count(other => CountEligible(other) < other.Target) == 1
                    ? waveSize : Math.Min(waveSize, state.Target - CountEligible(state));
                while (wave.Count < wanted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!state.Pending.TryDequeue(out var id))
                    {
                        if (state.NextQuery >= state.Queries.Count)
                        {
                            // Revisit unused first-query hits if refinement still
                            // leaves a gap; changing search priority must not lose them.
                            if (state.Deferred.TryDequeue(out var deferred))
                            {
                                state.Pending.Enqueue(deferred);
                                continue;
                            }
                            break;
                        }
                        var hits = await SearchRecipesAsync(state.Queries[state.NextQuery++], state.Request.Site, cancellationToken);
                        foreach (var hit in hits)
                            if (state.Seen.Add(hit.CandidateId)) state.Pending.Enqueue(hit.CandidateId);
                        progressed = true;
                        continue;
                    }
                    if (!completed.ContainsKey(id))
                    {
                        if (completed.Count + newCandidates >= maxResolvedCandidates) continue;
                        var matchReserve = state.Request.FoodRequest is null ? 0 : 1;
                        if (newCandidates >= (AiOperationBudget.Current?.RemainingAttempts ?? int.MaxValue) - _generationReserve - matchReserve) continue;
                        newCandidates++;
                    }
                    state.Researched.Add(id);
                    _candidates[id].ResearchConstraintIds.TryAdd(state.Request.ConstraintId, 0);
                    wave.Add(id);
                }
                if (wave.Count == 0) continue;
                var results = await Task.WhenAll(wave.Select(id => completed.TryGetValue(id, out var cached)
                    ? Task.FromResult(cached) : GetRecipeAsync(id, cancellationToken)));
                foreach (var result in results) completed[result.CandidateId] = result;
                if (state.Request.FoodRequest is { } foodRequest && _dishMatcher is not null
                    && (AiOperationBudget.Current?.RemainingAttempts ?? int.MaxValue) > _generationReserve)
                {
                    var candidates = results.Where(result => FactsEligible(result, state.Request))
                        .Select(result => new DishMatchCandidate(result.CandidateId, result.Title ?? "",
                            result.Ingredients ?? [], result.Instructions ?? [])).ToList();
                    if (candidates.Count > 0)
                    {
                        var matches = await _dishMatcher.MatchAsync(foodRequest, candidates, _requestId, cancellationToken);
                        foreach (var id in matches ?? []) matched[state.Request.ConstraintId].Add(id);
                    }
                }
                progressed = true;
            }
            if (!progressed) break;
        }
        foreach (var state in work)
        {
            var rejected = state.Researched.Select(id => completed[id]).ToList();
            _logger.LogInformation("[{RequestId}] Source requirement {ConstraintId}: {Eligible}/{Target} eligible from {Resolved} distinct candidates; failed={Failed}, unassessed={Unknown}, excluded={Excluded}",
                _requestId, state.Request.ConstraintId, CountEligible(state), state.Target, rejected.Count,
                rejected.Count(result => result.Error != null || !result.Scrapable),
                rejected.Count(result => !result.FactsAssessed),
                rejected.Count(result => (state.Request.ExcludedClasses ?? []).Any(result.ContainsClasses.Contains)));
        }

        // Only expose candidates that satisfy a researched clause. All other outcomes
        // remain in the session caches/metrics, rather than bloating the model prompt.
        var retained = completed.Values.Select(result => result with
        {
            ResearchConstraintIds = work.Where(state => _candidates[result.CandidateId].ResearchConstraintIds.ContainsKey(state.Request.ConstraintId)
                && Eligible(result, state.Request)).Select(state => state.Request.ConstraintId).Distinct().Order().ToList()
        }).Where(result => result.ResearchConstraintIds.Count > 0).ToList();
        foreach (var result in retained)
            foreach (var id in result.ResearchConstraintIds)
                _candidates[result.CandidateId].VerifiedResearchConstraintIds.TryAdd(id, 0);
        _logger.LogInformation("[{RequestId}] External research inspected {Resolved} candidates and retained {Eligible} for {Requirements} requirement(s)",
            _requestId, completed.Count, retained.Count, work.Count);
        return retained;

        bool Eligible(GetRecipeResult result, RecipeResearchRequest request) => FactsEligible(result, request)
            && (request.FoodRequest is null || matched[request.ConstraintId].Contains(result.CandidateId));

        static bool FactsEligible(GetRecipeResult result, RecipeResearchRequest request)
        {
            if (result.Error != null || !result.Scrapable || NormalizeHost(result.SourceSite) != NormalizeHost(request.Site)) return false;
            var required = request.RequiredClasses ?? [];
            var excluded = request.ExcludedClasses ?? [];
            if ((required.Count > 0 || excluded.Count > 0) && !result.FactsAssessed) return false;
            return required.All(result.ContainsClasses.Contains) && !excluded.Any(result.ContainsClasses.Contains);
        }
    }

    internal Task<IReadOnlyList<SearchHit>> SearchRecipesAsync(
        string query,
        string? site = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveSite = string.IsNullOrWhiteSpace(site) ? _defaultSite : site.Trim();
        var key = $"{query.Trim()}|{effectiveSite}";
        return _searchCache.GetOrAdd(key, _ => new(() => SearchRecipesCoreAsync(query, effectiveSite, cancellationToken))).Value;
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
        Interlocked.Increment(ref _searchCount);
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

            var candidate = _candidatesByUrl.GetOrAdd(url!, address =>
                new($"c{Interlocked.Increment(ref _candidateSequence)}", address, result.Title));
            _candidates.TryAdd(candidate.Id, candidate);
            hits.Add(new SearchHit(candidate.Id, result.Title, result.Snippet));
        }

        _logger.LogInformation(
            "[{RequestId}] Tool search_recipes(\"{Query}\", site={Site}) → {Count} candidates in {ElapsedMs}ms",
            _requestId, query, effectiveSite, hits.Count, stopwatch.ElapsedMilliseconds);
        Interlocked.Add(ref _searchDurationMs, stopwatch.ElapsedMilliseconds);
        Interlocked.Add(ref _searchResultCount, hits.Count);
        if (hits.Count == 0)
        {
            Interlocked.Increment(ref _emptySearchCount);
        }
        return hits;
    }

    internal Task<GetRecipeResult> GetRecipeAsync(
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        var key = candidateId.Trim();
        return _recipeCache.GetOrAdd(key, _ => new(() => GetRecipeCoreAsync(key, cancellationToken))).Value;
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
        Interlocked.Increment(ref _resolutionCount);
        var preview = await _importService.PreviewAsync(candidate.SearchUrl, cancellationToken);
        _logger.LogInformation(
            "[{RequestId}] Tool get_recipe({CandidateId}) → scrapable={Scrapable}, error={Error} in {ElapsedMs}ms",
            _requestId, candidateId, preview.Scrapable, preview.Error, stopwatch.ElapsedMilliseconds);

        if (preview.Error != null)
        {
            Interlocked.Increment(ref _resolutionFailureCount);
            Interlocked.Add(ref _resolutionDurationMs, stopwatch.ElapsedMilliseconds);
            return FailedRecipe(candidateId, preview.Error);
        }

        var recipe = preview.Recipe;
        var canonicalUrl = ResolveSourceUrl(recipe?.SourceUrl, candidate.SearchUrl);
        if (canonicalUrl == null || !TryNormalizeAllowedUrl(canonicalUrl, out canonicalUrl))
        {
            Interlocked.Increment(ref _resolutionFailureCount);
            Interlocked.Add(ref _resolutionDurationMs, stopwatch.ElapsedMilliseconds);
            return FailedRecipe(candidateId, "The recipe resolved outside the referenced source hosts.");
        }
        if (!preview.Scrapable || recipe == null
            || !RecipePageClassifier.IsPotentialRecipeUri(canonicalUrl))
        {
            Interlocked.Increment(ref _resolutionFailureCount);
            Interlocked.Add(ref _resolutionDurationMs, stopwatch.ElapsedMilliseconds);
            return FailedRecipe(candidateId, "The result could not be structurally verified as one importable recipe.");
        }

        IReadOnlyList<IngredientClass> facts = [];
        var factsAssessed = false;
        if (recipe != null && _factsExtractor?.IsAvailable == true)
        {
            var assessed = await _factsExtractor.ExtractAsync(
                recipe.Title, recipe.IngredientLines, cancellationToken, _requestId);
            if (assessed != null)
            {
                facts = assessed;
                factsAssessed = true;
            }
        }

        candidate.MarkFetched(canonicalUrl!, recipe, preview.Text, facts, factsAssessed);
        Interlocked.Add(ref _resolutionDurationMs, stopwatch.ElapsedMilliseconds);
        return new GetRecipeResult(
            CandidateId: candidateId,
            SourceSite: new Uri(canonicalUrl!).Host,
            Scrapable: preview.Scrapable,
            Title: recipe?.Title ?? candidate.SearchTitle,
            TotalTimeMinutes: recipe?.TotalTimeMinutes,
            PrepTimeMinutes: recipe?.PrepTimeMinutes,
            CookTimeMinutes: recipe?.CookTimeMinutes,
            Servings: recipe?.Servings,
            Ingredients: recipe?.IngredientLines,
            Instructions: recipe?.Steps,
            ContainsClasses: facts,
            FactsAssessed: factsAssessed,
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
                state.PageText,
                state.ContainsClasses,
                state.FactsAssessed,
                state.Recipe?.TotalTimeMinutes)
            { ResearchConstraintIds = state.VerifiedResearchConstraintIds.Keys.Order().ToList() };
            return true;
        }

        candidate = null;
        return false;
    }

    public ExternalRecipeSessionMetrics GetMetrics() => new(
        ResearchCalls: Volatile.Read(ref _researchCalls),
        SearchCount: Volatile.Read(ref _searchCount),
        EmptySearchCount: Volatile.Read(ref _emptySearchCount),
        SearchResultCount: Volatile.Read(ref _searchResultCount),
        ResolutionCount: Volatile.Read(ref _resolutionCount),
        ResolutionFailureCount: Volatile.Read(ref _resolutionFailureCount),
        SearchDurationMs: Interlocked.Read(ref _searchDurationMs),
        ResolutionDurationMs: Interlocked.Read(ref _resolutionDurationMs));

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
        SourceSite: null,
        Scrapable: false,
        Title: null,
        TotalTimeMinutes: null,
        PrepTimeMinutes: null,
        CookTimeMinutes: null,
        Servings: null,
        Ingredients: null,
        Instructions: null,
        ContainsClasses: [],
        FactsAssessed: false,
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

    private sealed class ResearchWork(RecipeResearchRequest request)
    {
        public RecipeResearchRequest Request { get; } = request;
        public int Target { get; } = Math.Clamp(request.CandidateCount, 1, 7);
        public List<string> Queries { get; } = new[] { request.Query }.Concat((request.AlternativeQueries ?? []).Take(3))
            .Where(query => !string.IsNullOrWhiteSpace(query)).Select(query => query.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        public int RefinementCount { get; set; }
        public int NextQuery { get; set; }
        public Queue<string> Pending { get; } = new();
        public Queue<string> Deferred { get; } = new();
        public HashSet<string> Seen { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Researched { get; } = new(StringComparer.OrdinalIgnoreCase);
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
        public IReadOnlyList<IngredientClass> ContainsClasses { get; private set; } = [];
        public bool FactsAssessed { get; private set; }
        public ConcurrentDictionary<string, byte> ResearchConstraintIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, byte> VerifiedResearchConstraintIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void MarkFetched(
            string canonicalUrl,
            ImportedRecipe? recipe,
            string? pageText,
            IReadOnlyList<IngredientClass> containsClasses,
            bool factsAssessed)
        {
            CanonicalUrl = canonicalUrl;
            Recipe = recipe;
            PageText = pageText;
            ContainsClasses = containsClasses;
            FactsAssessed = factsAssessed;
            Fetched = true;
        }
    }

    public record SearchHit(string CandidateId, string Title, string? Snippet);

    public record RecipeResearchRequest(
        string ConstraintId,
        string Query,
        string Site,
        int CandidateCount = 2,
        IReadOnlyList<string>? Dates = null,
        string? Course = null,
        IReadOnlyList<IngredientClass>? RequiredClasses = null,
        IReadOnlyList<IngredientClass>? ExcludedClasses = null,
        IReadOnlyList<string>? AlternativeQueries = null,
        string? FoodRequest = null);

    public record GetRecipeResult(
        string CandidateId,
        string? SourceSite,
        bool Scrapable,
        string? Title,
        int? TotalTimeMinutes,
        int? PrepTimeMinutes,
        int? CookTimeMinutes,
        int? Servings,
        IReadOnlyList<string>? Ingredients,
        IReadOnlyList<string>? Instructions,
        IReadOnlyList<IngredientClass> ContainsClasses,
        bool FactsAssessed,
        string? PageText,
        string? Error)
    {
        public IReadOnlyList<string> ResearchConstraintIds { get; init; } = [];
    }
}

public sealed record ExternalRecipeCandidate(
    string Id,
    string SourceUrl,
    string Title,
    IReadOnlyList<string> Ingredients,
    IReadOnlyList<string> Instructions,
    string? PageText,
    IReadOnlyList<IngredientClass> ContainsClasses,
    bool FactsAssessed,
    int? TotalTimeMinutes = null)
{
    public IReadOnlyList<string> ResearchConstraintIds { get; init; } = [];
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dishhive.Api.Services.WebSearch;

/// <summary>
/// Web search backed by a self-hosted SearXNG instance
/// (https://docs.searxng.org/). Uses the JSON output format, which the instance
/// must have enabled (settings.yml: search.formats includes "json"). Configured
/// by WebSearch:BaseUrl; disabled (BaseAddress null) leaves the client reporting
/// unconfigured so the search tool is not offered to the model.
/// </summary>
public class SearxngWebSearchClient : IWebSearchClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<SearxngWebSearchClient> _logger;

    public SearxngWebSearchClient(HttpClient httpClient, ILogger<SearxngWebSearchClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public bool IsConfigured => _httpClient.BaseAddress != null;

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query, string? site, int count, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        // Host restriction rides on the query as a "site:" operator — the engines
        // SearXNG proxies (DuckDuckGo, Google, ...) all understand it.
        var effectiveQuery = string.IsNullOrWhiteSpace(site)
            ? query.Trim()
            : $"site:{site.Trim()} {query.Trim()}";

        var url = $"search?q={Uri.EscapeDataString(effectiveQuery)}&format=json&safesearch=1";

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var response = await _httpClient.GetFromJsonAsync<SearxngResponse>(url, JsonOptions, cts.Token);
            var results = response?.Results ?? [];

            return results
                .Where(r => !string.IsNullOrWhiteSpace(r.Url) && !string.IsNullOrWhiteSpace(r.Title))
                .Take(Math.Max(1, count))
                .Select(r => new WebSearchResult(r.Title!.Trim(), r.Url!.Trim(), r.Content?.Trim()))
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Search failures must never break planning — the model just gets no hits
            _logger.LogWarning(ex, "SearXNG search failed for query {Query}", effectiveQuery);
            return [];
        }
    }

    private sealed record SearxngResponse([property: JsonPropertyName("results")] List<SearxngResult>? Results);

    private sealed record SearxngResult(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("content")] string? Content);
}

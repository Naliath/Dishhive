namespace Dishhive.Api.Services.WebSearch;

/// <summary>
/// Configuration for the optional web-search tool exposed to the AI planner
/// ("WebSearch" section, WebSearch__* env vars in Docker). Disabled when no
/// provider/BaseUrl is configured — the NoOp client stays registered (Freezy pattern),
/// so the suggestion service simply doesn't attach the search tool.
/// See docs/features/ai-week-planning.md.
/// </summary>
public class WebSearchOptions
{
    public const string SectionName = "WebSearch";

    /// <summary>
    /// searxng (self-hosted, no key). The seam is provider-agnostic; other
    /// providers (brave, tavily, ...) can be added without touching callers.
    /// </summary>
    public string Provider { get; set; } = "";

    /// <summary>
    /// Base URL of the search backend. For SearXNG this is the instance root
    /// (e.g. http://searxng:8080), whose JSON API lives at /search?format=json.
    /// </summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Optional API key for providers that need one (unused by SearXNG)</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Upper bound on results returned to the model per query</summary>
    public int MaxResults { get; set; } = 6;

    public string NormalizedProvider => Provider.Trim().ToLowerInvariant();

    /// <summary>Configured when a provider and a base URL are both set</summary>
    public bool IsConfigured =>
        NormalizedProvider.Length > 0 && !string.IsNullOrWhiteSpace(BaseUrl);
}

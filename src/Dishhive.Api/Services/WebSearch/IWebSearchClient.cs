namespace Dishhive.Api.Services.WebSearch;

/// <summary>
/// Web search behind a provider-agnostic seam. The AI planner exposes this as a
/// tool so models without native web search can still find recipes on the web
/// (see ExternalRecipeTools). Disabled — and the tool omitted — when unconfigured.
/// </summary>
public interface IWebSearchClient
{
    /// <summary>Whether search is configured (drives whether the tool is offered)</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Runs a web search. When <paramref name="site"/> is set, results are
    /// restricted to that host (e.g. "dagelijksekost.vrt.be"). Returns at most
    /// <paramref name="count"/> results; an empty list when nothing was found or
    /// the backend is unreachable (search failures never break planning).
    /// </summary>
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query, string? site, int count, CancellationToken cancellationToken = default);
}

/// <summary>A single web-search hit</summary>
public record WebSearchResult(string Title, string Url, string? Snippet);

/// <summary>Default when web search is not configured: feature reports disabled, yields nothing</summary>
public class NoOpWebSearchClient : IWebSearchClient
{
    public bool IsConfigured => false;

    public Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query, string? site, int count, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<WebSearchResult>>([]);
}

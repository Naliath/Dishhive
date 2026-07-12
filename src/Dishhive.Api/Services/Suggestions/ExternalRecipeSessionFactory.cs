using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.WebSearch;
using Dishhive.Api.Services.Facts;
using Microsoft.Extensions.AI;

namespace Dishhive.Api.Services.Suggestions;

public interface IExternalRecipeSession
{
    IList<AITool> BuildTools();
    Task<IReadOnlyList<ExternalRecipeTools.GetRecipeResult>> ResearchAsync(
        List<ExternalRecipeTools.RecipeResearchRequest> requests,
        CancellationToken cancellationToken = default);
    bool TryResolveCandidate(string? candidateId, out ExternalRecipeCandidate? candidate);
    ExternalRecipeSessionMetrics GetMetrics();
}

public sealed record ExternalRecipeSessionMetrics(
    int ResearchCalls,
    int SearchCount,
    int EmptySearchCount,
    int SearchResultCount,
    int ResolutionCount,
    int ResolutionFailureCount,
    long SearchDurationMs,
    long ResolutionDurationMs);

public interface IExternalRecipeSessionFactory
{
    bool IsConfigured { get; }

    IExternalRecipeSession Create(
        IReadOnlyList<SourceConstraint> sourceConstraints,
        string requestId,
        ILogger logger);
}

public sealed class ExternalRecipeSessionFactory(
    IWebSearchClient webSearch,
    IRecipeImportService importService,
    IRecipeFactsExtractor factsExtractor,
    WebSearchOptions options) : IExternalRecipeSessionFactory
{
    public ExternalRecipeSessionFactory(
        IWebSearchClient webSearch,
        IRecipeImportService importService,
        WebSearchOptions options)
        : this(webSearch, importService, new NoOpRecipeFactsExtractor(), options)
    {
    }

    public bool IsConfigured => webSearch.IsConfigured;

    public IExternalRecipeSession Create(
        IReadOnlyList<SourceConstraint> sourceConstraints,
        string requestId,
        ILogger logger)
    {
        var hosts = sourceConstraints.Select(constraint => constraint.Host).Distinct().ToList();
        return new ExternalRecipeTools(
            webSearch,
            importService,
            options.MaxResults,
            hosts.Count == 1 ? hosts[0] : null,
            hosts,
            requestId,
            logger,
            factsExtractor);
    }
}

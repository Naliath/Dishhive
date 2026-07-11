using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.WebSearch;
using Microsoft.Extensions.AI;

namespace Dishhive.Api.Services.Suggestions;

public interface IExternalRecipeSession
{
    IList<AITool> BuildTools();
    bool TryResolveCandidate(string? candidateId, out ExternalRecipeCandidate? candidate);
}

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
    WebSearchOptions options) : IExternalRecipeSessionFactory
{
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
            logger);
    }
}

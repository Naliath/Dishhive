using Dishhive.Api.Services.Agents.RecipeImport;

namespace Dishhive.Api.Services.RecipeImport;

public interface IRecipeImportService
{
    Task<ImportPreviewResult> PreviewAsync(Uri url, CancellationToken cancellationToken = default);
    IReadOnlyList<string> SupportedProviderKeys { get; }
}

/// <summary>
/// Result of a recipe import preview. <see cref="UsedAgent"/> is true when the LLM-powered
/// fallback was used; <see cref="BlueprintLearned"/> reports whether a reusable static
/// blueprint was successfully stored for next time.
/// </summary>
public sealed record ImportPreviewResult(
    ImportedRecipe Recipe,
    bool UsedAgent,
    bool BlueprintLearned,
    string? AgentNote);

public sealed class RecipeImportService : IRecipeImportService
{
    private readonly IRecipeSourceRegistry _registry;
    private readonly IRecipeImportAgent _agent;
    private readonly ILogger<RecipeImportService> _logger;

    public RecipeImportService(
        IRecipeSourceRegistry registry,
        IRecipeImportAgent agent,
        ILogger<RecipeImportService> logger)
    {
        _registry = registry;
        _agent = agent;
        _logger = logger;
    }

    public IReadOnlyList<string> SupportedProviderKeys =>
        _registry.All.Select(p => p.ProviderKey).ToList();

    public async Task<ImportPreviewResult> PreviewAsync(Uri url, CancellationToken cancellationToken = default)
    {
        var provider = _registry.FindFor(url);
        if (provider is not null)
        {
            _logger.LogInformation("Importing recipe from {Url} via {Provider}", url, provider.ProviderKey);
            var imported = await provider.FetchAsync(url, cancellationToken);
            return new ImportPreviewResult(imported, UsedAgent: false, BlueprintLearned: false, AgentNote: null);
        }

        if (_agent.IsAvailable)
        {
            _logger.LogInformation("No static provider matched {Url}; escalating to recipe-import agent.", url);
            var result = await _agent.ImportAsync(url, cancellationToken);
            return new ImportPreviewResult(result.Recipe, UsedAgent: true, BlueprintLearned: result.BlueprintSaved, AgentNote: result.BlueprintNote);
        }

        throw new NotSupportedException(
            $"No recipe source provider supports host '{url.Host}', and the AI fallback is disabled. " +
            $"Supported static providers: {string.Join(", ", SupportedProviderKeys)}.");
    }
}

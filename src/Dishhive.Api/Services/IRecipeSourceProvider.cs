using Dishhive.Api.Models.DTOs;

namespace Dishhive.Api.Services;

/// <summary>
/// Implemented by each recipe import source provider.
/// New sources can be added by implementing this interface and registering via DI.
/// </summary>
public interface IRecipeSourceProvider
{
    /// <summary>Display name of this source (e.g., "DagelijkseKost").</summary>
    string SourceName { get; }

    /// <summary>Returns true if this provider can handle the given URL.</summary>
    bool CanHandle(string url);

    /// <summary>Fetches and parses a recipe from the given URL.</summary>
    Task<ImportedRecipeDto?> ImportFromUrlAsync(string url, CancellationToken cancellationToken = default);
}

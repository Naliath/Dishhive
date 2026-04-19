using Dishhive.Api.Models.DTOs;

namespace Dishhive.Api.Services;

/// <summary>
/// Orchestrates recipe import by selecting the correct provider for a given URL.
/// </summary>
public interface IRecipeImportService
{
    /// <summary>
    /// Imports a recipe from the given URL.
    /// Returns null if no provider supports the URL or if extraction fails.
    /// </summary>
    Task<ImportedRecipeDto?> ImportAsync(string url, CancellationToken cancellationToken = default);
}

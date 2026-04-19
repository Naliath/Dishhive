using Dishhive.Api.Models.DTOs;

namespace Dishhive.Api.Services;

/// <summary>
/// Orchestrates recipe import by finding the first provider that can handle the URL.
/// </summary>
public class RecipeImportService : IRecipeImportService
{
    private readonly IEnumerable<IRecipeSourceProvider> _providers;
    private readonly ImageDownloadService _imageDownloader;
    private readonly ILogger<RecipeImportService> _logger;

    public RecipeImportService(
        IEnumerable<IRecipeSourceProvider> providers,
        ImageDownloadService imageDownloader,
        ILogger<RecipeImportService> logger)
    {
        _providers = providers;
        _imageDownloader = imageDownloader;
        _logger = logger;
    }

    public async Task<ImportedRecipeDto?> ImportAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogWarning("Import requested with empty URL");
            return null;
        }

        var provider = _providers.FirstOrDefault(p => p.CanHandle(url));
        if (provider == null)
        {
            _logger.LogWarning("No recipe source provider found for URL: {Url}", url);
            return null;
        }

        _logger.LogInformation("Importing recipe from {Url} using provider {Provider}", url, provider.SourceName);

        try
        {
            var result = await provider.ImportFromUrlAsync(url, cancellationToken);
            if (result == null)
                return null;

            // Download the picture and store it as a Base64 data URI so it is always
            // available locally, independent of the external CDN.
            if (!string.IsNullOrWhiteSpace(result.PictureUrl))
            {
                var dataUri = await _imageDownloader.DownloadAndEncodeAsync(result.PictureUrl, cancellationToken);
                if (dataUri != null)
                    result = result with { PictureUrl = dataUri };
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error importing recipe from {Url}", url);
            return null;
        }
    }
}

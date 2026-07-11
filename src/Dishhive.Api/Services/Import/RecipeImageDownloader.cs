using Dishhive.Api.Models;

namespace Dishhive.Api.Services.Import;

/// <summary>
/// Downloads a recipe's image and stores the bytes on the entity so recipes don't depend
/// on expiring source URLs (signed Google Storage links on Dagelijkse Kost).
/// Failures are logged and tolerated: the remote URL remains as fallback.
/// Shared between URL import and file import.
/// </summary>
public static class RecipeImageDownloader
{
    // Kept as an alias for the exchange importer and older callers.
    public const int MaxImageBytes = RecipeImageProcessor.MaxSourceBytes;

    public static async Task<ProcessedRecipeImage> DownloadAsync(
        ISafeHttpFetcher httpFetcher, Uri imageUri, CancellationToken cancellationToken)
    {
        var resource = await httpFetcher.GetAsync(
            imageUri.AbsoluteUri,
            RecipeImageProcessor.MaxSourceBytes,
            cancellationToken);
        var contentType = resource.ContentType;
        if (contentType == null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new RecipeImageException(
                $"The URL returned '{contentType ?? "no content type"}' instead of an image.");
        }

        await using var stream = new MemoryStream(resource.Content, writable: false);
        return await RecipeImageProcessor.ProcessAsync(stream, cancellationToken);
    }

    public static async Task TryDownloadAsync(
        ISafeHttpFetcher httpFetcher, Recipe recipe, ILogger logger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recipe.ImageUrl)
            || !Uri.TryCreate(recipe.ImageUrl, UriKind.Absolute, out var imageUri)
            || (imageUri.Scheme != Uri.UriSchemeHttp && imageUri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            var processed = await DownloadAsync(httpFetcher, imageUri, cancellationToken);
            recipe.ImageData = processed.Data;
            recipe.ImageContentType = processed.ContentType;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or RecipeImageException)
        {
            logger.LogWarning(ex, "Could not download recipe image {Url}; keeping remote URL only", imageUri);
        }
    }
}

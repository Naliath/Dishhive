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
    public const int MaxImageBytes = 5 * 1024 * 1024;

    public static async Task TryDownloadAsync(
        HttpClient httpClient, Recipe recipe, ILogger logger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recipe.ImageUrl)
            || !Uri.TryCreate(recipe.ImageUrl, UriKind.Absolute, out var imageUri)
            || (imageUri.Scheme != Uri.UriSchemeHttp && imageUri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            using var response = await httpClient.GetAsync(imageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType == null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Recipe image {Url} has non-image content type {ContentType}; skipping download",
                    imageUri, contentType ?? "(none)");
                return;
            }

            if (response.Content.Headers.ContentLength > MaxImageBytes)
            {
                logger.LogWarning("Recipe image {Url} exceeds {MaxBytes} bytes; skipping download", imageUri, MaxImageBytes);
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0 || bytes.Length > MaxImageBytes)
            {
                return;
            }

            recipe.ImageData = bytes;
            recipe.ImageContentType = contentType;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not download recipe image {Url}; keeping remote URL only", imageUri);
        }
    }
}

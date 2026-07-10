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
        HttpClient httpClient, Uri imageUri, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            imageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType == null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new RecipeImageException(
                $"The URL returned '{contentType ?? "no content type"}' instead of an image.");
        }

        if (response.Content.Headers.ContentLength > RecipeImageProcessor.MaxSourceBytes)
        {
            throw new RecipeImageException(
                $"Images may be at most {RecipeImageProcessor.MaxSourceBytes / 1024 / 1024} MB.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await RecipeImageProcessor.ProcessAsync(stream, cancellationToken);
    }

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
            var processed = await DownloadAsync(httpClient, imageUri, cancellationToken);
            recipe.ImageData = processed.Data;
            recipe.ImageContentType = processed.ContentType;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or RecipeImageException)
        {
            logger.LogWarning(ex, "Could not download recipe image {Url}; keeping remote URL only", imageUri);
        }
    }
}

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Dishhive.Api.Services;

/// <summary>
/// Downloads an image from a URL, resizes it to fit within a bounding box, and returns
/// it as a Base64-encoded JPEG data URI suitable for storage and embedding in HTML.
/// </summary>
public class ImageDownloadService
{
    private const int MaxDimension = 800;
    private const int JpegQuality = 82;

    private readonly HttpClient _httpClient;
    private readonly ILogger<ImageDownloadService> _logger;

    public ImageDownloadService(HttpClient httpClient, ILogger<ImageDownloadService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Downloads the image at <paramref name="url"/>, resizes it so neither dimension
    /// exceeds <see cref="MaxDimension"/>, and returns a JPEG data URI string
    /// (<c>data:image/jpeg;base64,…</c>). Returns <c>null</c> if the download or
    /// processing fails.
    /// </summary>
    public async Task<string?> DownloadAndEncodeAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var image = await Image.LoadAsync(stream, cancellationToken);

            // Resize only if the image is larger than the bounding box
            if (image.Width > MaxDimension || image.Height > MaxDimension)
            {
                image.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size(MaxDimension, MaxDimension),
                    Mode = ResizeMode.Max,
                }));
            }

            using var output = new MemoryStream();
            await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = JpegQuality }, cancellationToken);

            var base64 = Convert.ToBase64String(output.ToArray());
            return $"data:image/jpeg;base64,{base64}";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to download image from {Url}", url);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process image from {Url}", url);
            return null;
        }
    }
}

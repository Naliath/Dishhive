using SkiaSharp;

namespace Dishhive.Api.Services.Import;

/// <summary>
/// Validates and normalizes untrusted recipe images. Every stored image uses the same
/// bounded, browser-friendly representation regardless of whether it came from an import,
/// a local file or the device camera.
/// </summary>
public static class RecipeImageProcessor
{
    public const int MaxSourceBytes = 15 * 1024 * 1024;
    public const int MaxStoredBytes = 5 * 1024 * 1024;
    public const int MaxDimension = 1600;
    public const int WebpQuality = 82;
    public const string StoredContentType = "image/webp";
    private const long MaxDecodedPixels = 50_000_000;

    public static async Task<ProcessedRecipeImage> ProcessAsync(
        Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var boundedSource = new MemoryStream();
        await CopyWithLimitAsync(source, boundedSource, MaxSourceBytes, cancellationToken);
        if (boundedSource.Length == 0)
        {
            throw new RecipeImageException("The image file is empty.");
        }

        boundedSource.Position = 0;
        try
        {
            using var encodedData = SKData.CreateCopy(boundedSource.ToArray());
            using var codec = SKCodec.Create(encodedData);
            if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            {
                throw new RecipeImageException("The file is not a supported or valid image.");
            }
            if ((long)codec.Info.Width * codec.Info.Height > MaxDecodedPixels)
            {
                throw new RecipeImageException("The image resolution is too large to process safely.");
            }

            var swapsAxes = SwapsAxes(codec.EncodedOrigin);
            var sourceOrientedWidth = swapsAxes ? codec.Info.Height : codec.Info.Width;
            var sourceOrientedHeight = swapsAxes ? codec.Info.Width : codec.Info.Height;
            var storageScale = Math.Min(1D,
                (double)MaxDimension / Math.Max(sourceOrientedWidth, sourceOrientedHeight));

            // JPEG and several other codecs can decode close to the requested size,
            // avoiding a full-resolution pixel buffer. Other formats safely fall back
            // to their native dimensions after the pixel-count guard above.
            var decodedSize = codec.GetScaledDimensions((float)storageScale);
            var decodedInfo = new SKImageInfo(
                decodedSize.Width,
                decodedSize.Height,
                SKColorType.Rgba8888,
                SKAlphaType.Premul);
            using var decoded = new SKBitmap(decodedInfo);
            var decodeResult = codec.GetPixels(decodedInfo, decoded.GetPixels());
            if (decodeResult is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                throw new RecipeImageException("The file is not a supported or valid image.");
            }

            using var oriented = ApplyOrientation(decoded, codec.EncodedOrigin);
            var finalScale = Math.Min(1D,
                (double)MaxDimension / Math.Max(oriented.Width, oriented.Height));
            var finalWidth = Math.Max(1, (int)Math.Round(oriented.Width * finalScale));
            var finalHeight = Math.Max(1, (int)Math.Round(oriented.Height * finalScale));

            using var resized = new SKBitmap(new SKImageInfo(
                finalWidth, finalHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(resized))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(
                    oriented,
                    new SKRect(0, 0, oriented.Width, oriented.Height),
                    new SKRect(0, 0, finalWidth, finalHeight),
                    new SKSamplingOptions(SKCubicResampler.Mitchell));
                canvas.Flush();
            }

            using var image = SKImage.FromBitmap(resized);
            using var output = image.Encode(SKEncodedImageFormat.Webp, WebpQuality);
            if (output == null || output.Size == 0 || output.Size > MaxStoredBytes)
            {
                throw new RecipeImageException("The processed image is too large to store.");
            }

            return new ProcessedRecipeImage(
                output.ToArray(), StoredContentType, finalWidth, finalHeight);
        }
        catch (RecipeImageException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new RecipeImageException("The file is not a supported or valid image.", ex);
        }
    }

    private static bool SwapsAxes(SKEncodedOrigin origin) => origin is
        SKEncodedOrigin.LeftTop or
        SKEncodedOrigin.RightTop or
        SKEncodedOrigin.RightBottom or
        SKEncodedOrigin.LeftBottom;

    /// <summary>Applies the EXIF orientation reported by the codec and drops the metadata</summary>
    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        var swapsAxes = SwapsAxes(origin);
        var target = new SKBitmap(new SKImageInfo(
            swapsAxes ? source.Height : source.Width,
            swapsAxes ? source.Width : source.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));

        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(OrientationMatrix(origin, source.Width, source.Height));
        canvas.DrawBitmap(
            source,
            SKPoint.Empty,
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
            null);
        canvas.Flush();
        return target;
    }

    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
        _ => SKMatrix.Identity
    };

    private static async Task CopyWithLimitAsync(
        Stream source, Stream destination, int maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return;
            }

            total += read;
            if (total > maximumBytes)
            {
                throw new RecipeImageException($"Images may be at most {maximumBytes / 1024 / 1024} MB.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}

public sealed record ProcessedRecipeImage(byte[] Data, string ContentType, int Width, int Height);

public sealed class RecipeImageException : Exception
{
    public RecipeImageException(string message) : base(message)
    {
    }

    public RecipeImageException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

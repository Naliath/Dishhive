using Dishhive.Api.Services.Import;
using FluentAssertions;
using SkiaSharp;

namespace Dishhive.Api.Tests.Services;

public class RecipeImageProcessorTests
{
    [Fact]
    public async Task Process_LargeImage_ResizesAndEncodesWebp()
    {
        await using var source = new MemoryStream();
        using (var bitmap = new SKBitmap(new SKImageInfo(2400, 1200)))
        using (var canvas = new SKCanvas(bitmap))
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            canvas.Clear(SKColors.Orange);
            canvas.Flush();
            await source.WriteAsync(data.ToArray());
        }
        source.Position = 0;

        var processed = await RecipeImageProcessor.ProcessAsync(source);

        processed.ContentType.Should().Be("image/webp");
        processed.Width.Should().Be(1600);
        processed.Height.Should().Be(800);
        processed.Data.Should().StartWith([(byte)'R', (byte)'I', (byte)'F', (byte)'F']);
        processed.Data.Length.Should().BeLessThan(RecipeImageProcessor.MaxStoredBytes);
    }

    [Fact]
    public async Task Process_NonImage_ThrowsFriendlyException()
    {
        await using var source = new MemoryStream("not an image"u8.ToArray());

        var act = () => RecipeImageProcessor.ProcessAsync(source);

        await act.Should().ThrowAsync<RecipeImageException>()
            .WithMessage("*supported or valid image*");
    }
}

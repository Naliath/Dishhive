using System.Text.Json.Nodes;
using Dishhive.Api.Services.Demo;
using SkiaSharp;

namespace Dishhive.Api.Tests.Services;

public sealed class DemoSeedImagesTests
{
    [Theory]
    [InlineData(2000, 1000, 1000, 500)]
    [InlineData(1000, 1600, 625, 1000)]
    [InlineData(400, 600, 400, 600)]
    public async Task Optimize_ResizesOnlyThePhoto_AndPreservesRecipeFacts(
        int width, int height, int expectedWidth, int expectedHeight)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.Orange);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var input = $$"""
            {
              "@context": "https://schema.org",
              "@graph": [{
                "@type": "Recipe",
                "name": "Crème brûlée",
                "url": "https://example.com/recipe",
                "recipeIngredient": ["200 ml room", "2 eieren"],
                "recipeInstructions": [{ "@type": "HowToStep", "text": "Meng en bak." }],
                "dishhive:dietaryFacts": { "status": "aiDetected", "contains": ["Milk", "Eggs"] },
                "dishhive:collections": ["Desserts & baking"],
                "image": "data:image/png;base64,{{Convert.ToBase64String(png.ToArray())}}"
              }]
            }
            """;

        var result = JsonNode.Parse(await DemoSeedImages.OptimizeAsync(input))!;
        var dataUri = result["@graph"]![0]!["image"]!.GetValue<string>();
        Assert.StartsWith("data:image/webp;base64,", dataUri);
        using var photo = SKBitmap.Decode(Convert.FromBase64String(dataUri.Split(',', 2)[1]));
        Assert.Equal(expectedWidth, photo.Width);
        Assert.Equal(expectedHeight, photo.Height);

        var original = JsonNode.Parse(input)!;
        original["@graph"]![0]!.AsObject().Remove("image");
        result["@graph"]![0]!.AsObject().Remove("image");
        Assert.True(JsonNode.DeepEquals(original, result));
    }

    [Fact]
    public async Task Optimize_RejectsRemoteImagesInsteadOfDroppingThePhoto()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => DemoSeedImages.OptimizeAsync(
            """{"@graph":[{"name":"Soup","image":"https://example.com/photo.webp"}]}"""));
    }
}

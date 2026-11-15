using Dishhive.Api.Services.Ai;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Suggestions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
using SkiaSharp;

namespace Dishhive.Api.Tests.Services;

/// <summary>Opt-in smoke check; never uses network in ordinary verification runs.</summary>
public sealed class RecipeContentLiveTests(ITestOutputHelper output)
{
    private sealed class LiveImportFactAttribute : FactAttribute
    {
        public LiveImportFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("DISHHIVE_LIVE_IMPORT") != "1")
                Skip = "Set DISHHIVE_LIVE_IMPORT=1 and AI_* configuration to verify the configured model.";
        }
    }

    [LiveImportFact]
    public async Task ConfiguredModel_ExtractsRecipeTextAndReportsIndependentImageCapability()
    {
        var options = new AiOptions
        {
            Provider = Environment.GetEnvironmentVariable("AI_PROVIDER") ?? "",
            Model = Environment.GetEnvironmentVariable("AI_MODEL") ?? "",
            BaseUrl = Environment.GetEnvironmentVariable("AI_BASE_URL") ?? "",
            ApiKey = Environment.GetEnvironmentVariable("AI_API_KEY") ?? ""
        };
        Assert.True(options.IsConfigured);
        using var client = ChatClientFactory.Create(options);
        var gate = new AiExecutionGate(client, options, new NoOpAiTaskMetricsStore(), NullLogger<AiExecutionGate>.Instance);
        var extractor = new RecipeContentExtractor(gate, options);
        var recipe = await extractor.ExtractAsync("Pancakes. Serves 2. Ingredients: 200 g flour, 250 ml milk, 1 egg. Mix all ingredients. Cook small pancakes in a hot nonstick pan for 2 minutes per side.",
            null, "live-import-smoke", default);
        Assert.Equal(3, recipe.IngredientLines.Count);
        Assert.NotEmpty(recipe.Steps); Assert.Equal(2, recipe.Servings);
        output.WriteLine("Text extraction succeeded; title: " + recipe.Title);
        var vision = new RecipeImageCapability(gate, options);
        var state = await vision.CheckAsync(default);
        output.WriteLine("Configured model image capability: " + state);
        Assert.Contains(state, new[] { "available", "unsupported", "unavailable" });
        if (state == "available")
        {
            using var bitmap = new SKBitmap(700, 360);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var font = new SKFont(SKTypeface.FromFamilyName("Arial"), 26);
            string[] lines = ["Pancakes - serves 2", "Ingredients:", "200 g flour", "250 ml milk", "1 egg", "Instructions:", "Mix all ingredients.", "Cook in a hot nonstick pan for 2 minutes per side."];
            for (var i = 0; i < lines.Length; i++) canvas.DrawText(lines[i], 20, 35 + i * 36, SKTextAlign.Left, font, paint);
            using var picture = SKImage.FromBitmap(bitmap);
            using var encoded = picture.Encode(SKEncodedImageFormat.Png, 100);
            await using var stream = new MemoryStream(encoded.ToArray());
            var image = await RecipeImageProcessor.ProcessAsync(stream, 2048, 90, default);
            var fromPhoto = await extractor.ExtractAsync(null, image, "live-photo-smoke", default);
            Assert.Equal(3, fromPhoto.IngredientLines.Count); Assert.NotEmpty(fromPhoto.Steps);
            Assert.Equal(2, fromPhoto.Servings);
            output.WriteLine("Recipe image extraction succeeded; title: " + fromPhoto.Title);
        }
    }
}

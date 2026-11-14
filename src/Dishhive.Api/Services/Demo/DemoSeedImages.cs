using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dishhive.Api.Services.Import;

namespace Dishhive.Api.Services.Demo;

/// <summary>Applies the demo's smaller photo profile to an exported recipe document.</summary>
internal static class DemoSeedImages
{
    internal const int MaxDimension = 1000;
    internal const int WebpQuality = 75;

    internal static async Task<string> OptimizeAsync(string json, CancellationToken cancellationToken = default)
    {
        var document = JsonNode.Parse(json)
            ?? throw new InvalidDataException("The demo recipe document is empty.");
        var recipes = document["@graph"]?.AsArray()
            ?? throw new InvalidDataException("The demo recipe graph is missing.");
        foreach (var recipe in recipes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = recipe?["image"]?.GetValue<string>();
            var separator = image?.IndexOf(',') ?? -1;
            if (image is null || !image.StartsWith("data:image/", StringComparison.Ordinal)
                || separator < 0 || !image[..separator].EndsWith(";base64", StringComparison.Ordinal))
                throw new InvalidDataException("Every demo recipe must have an embedded image.");
            using var source = new MemoryStream(Convert.FromBase64String(image[(separator + 1)..]));
            var processed = await RecipeImageProcessor.ProcessAsync(source, MaxDimension, WebpQuality, cancellationToken);
            recipe!["image"] = $"data:{processed.ContentType};base64,{Convert.ToBase64String(processed.Data)}";
        }
        return document.ToJsonString(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }
}

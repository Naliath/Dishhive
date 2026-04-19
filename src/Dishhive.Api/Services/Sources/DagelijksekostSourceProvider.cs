using Dishhive.Api.Models.DTOs;
using HtmlAgilityPack;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dishhive.Api.Services.Sources;

/// <summary>
/// Recipe import provider for https://dagelijksekost.vrt.be/
/// Extracts recipe data from schema.org/Recipe JSON-LD embedded in the page.
/// Falls back to basic HTML extraction if JSON-LD is absent or incomplete.
/// </summary>
public class DagelijksekostSourceProvider : IRecipeSourceProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DagelijksekostSourceProvider> _logger;

    private static readonly string[] SupportedHosts = ["dagelijksekost.vrt.be", "www.dagelijksekost.vrt.be"];

    public string SourceName => "DagelijkseKost";

    public DagelijksekostSourceProvider(HttpClient httpClient, ILogger<DagelijksekostSourceProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public bool CanHandle(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return SupportedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ImportedRecipeDto?> ImportFromUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching recipe from Dagelijkse Kost: {Url}", url);
            var html = await _httpClient.GetStringAsync(url, cancellationToken);
            return ParseHtml(html, url);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP error fetching recipe from {Url}", url);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing recipe from {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Parses an HTML string and extracts recipe data.
    /// Internal and virtual to support testing with HTML fixtures.
    /// </summary>
    public ImportedRecipeDto? ParseHtml(string html, string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Try JSON-LD first (most reliable for title, description, ingredients, etc.)
            var jsonLd = ExtractRecipeJsonLd(doc);
            if (jsonLd.HasValue)
            {
                _logger.LogInformation("Extracting recipe via JSON-LD for {Url}", sourceUrl);
                var result = ParseFromJsonLd(jsonLd.Value, sourceUrl);

                // Dagelijkse Kost truncates recipeInstructions in JSON-LD to only the first
                // two steps. The full step list is embedded in the Next.js RSC payload as
                // "instructions":{"0":"step","1":"step",...}. Use it when JSON-LD seems short.
                if (result.Steps.Count < 3)
                {
                    var rscSteps = ExtractStepsFromRscPayload(html);
                    if (rscSteps.Count > result.Steps.Count)
                    {
                        _logger.LogInformation(
                            "Supplementing {JsonLdCount} JSON-LD steps with {RscCount} steps from RSC payload for {Url}",
                            result.Steps.Count, rscSteps.Count, sourceUrl);
                        result = result with { Steps = rscSteps };
                    }
                }

                return result;
            }

            _logger.LogWarning("No JSON-LD Recipe found for {Url}", sourceUrl);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing HTML for {Url}", sourceUrl);
            return null;
        }
    }

    /// <summary>
    /// Extracts recipe steps from the Next.js RSC flight payload embedded in the page.
    /// Dagelijkse Kost stores the full step list as:
    ///   "instructions":{"0":"step text","1":"step text",...}
    /// inside a <c>self.__next_f.push</c> script, JSON-escaped with backslash quotes.
    /// </summary>
    private static List<string> ExtractStepsFromRscPayload(string html)
    {
        // The RSC payload is a JavaScript string where JSON is backslash-escaped.
        // We look for the instructions object whose keys are purely numeric.
        var match = Regex.Match(html,
            @"\\""instructions\\"":\{\\""0\\"":\\""",
            RegexOptions.None);

        if (!match.Success)
            return [];

        // Collect consecutive numeric-keyed steps starting at 0.
        var steps = new List<string>();
        int index = 0;
        while (true)
        {
            // Pattern: "N":"<escaped text>"
            var stepPattern = $@"\\""{index}\\"":\\""((?:[^\\\\\\""]|\\\\.)*)\\""";  
            var stepMatch = Regex.Match(html.Substring(match.Index), stepPattern);
            if (!stepMatch.Success)
                break;

            var rawValue = stepMatch.Groups[1].Value;
            // Unescape \" → " and \\ → \
            var step = rawValue.Replace("\\\"", "\"").Replace("\\\\", "\\");
            if (!string.IsNullOrWhiteSpace(step))
                steps.Add(step);
            index++;
        }

        return steps;
    }

    private static JsonElement? ExtractRecipeJsonLd(HtmlDocument doc)
    {
        var scriptNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scriptNodes == null)
            return null;

        foreach (var node in scriptNodes)
        {
            try
            {
                // Use InnerHtml (not InnerText) to get the raw script content without
                // HtmlAgilityPack decoding HTML entities, which would corrupt the JSON.
                var json = node.InnerHtml?.Trim();
                if (string.IsNullOrWhiteSpace(json))
                    continue;

                // Clone the JsonElement so it remains valid after the JsonDocument is disposed.
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement.Clone();

                // Handle both single object and array of objects
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        if (IsRecipeType(item))
                            return item;
                    }
                }
                else if (IsRecipeType(root))
                {
                    return root;
                }
            }
            catch (JsonException)
            {
                // Skip malformed JSON-LD blocks
            }
        }

        return null;
    }

    private static bool IsRecipeType(JsonElement element)
    {
        if (!element.TryGetProperty("@type", out var type))
            return false;

        // @type can be string or array
        if (type.ValueKind == JsonValueKind.String)
            return type.GetString()?.Equals("Recipe", StringComparison.OrdinalIgnoreCase) == true;

        if (type.ValueKind == JsonValueKind.Array)
            return type.EnumerateArray().Any(t => t.GetString()?.Equals("Recipe", StringComparison.OrdinalIgnoreCase) == true);

        return false;
    }

    private ImportedRecipeDto ParseFromJsonLd(JsonElement recipe, string sourceUrl)
    {
        var title = GetStringFromElement(recipe, "name") ?? "Untitled Recipe";
        var description = GetStringFromElement(recipe, "description");
        var servings = ParseServings(recipe);
        var pictureUrl = ExtractImageUrl(recipe);
        var videoUrl = ExtractVideoUrl(recipe);
        var rawData = recipe.GetRawText();

        var ingredients = ExtractIngredients(recipe);
        var steps = ExtractSteps(recipe);

        return new ImportedRecipeDto(
            Title: title,
            Description: description,
            Ingredients: ingredients,
            Steps: steps,
            Servings: servings,
            PictureUrl: pictureUrl,
            VideoUrl: videoUrl,
            SourceUrl: sourceUrl,
            SourceName: SourceName,
            SourceRawData: rawData
        );
    }

    private static string? GetStringFromElement(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return null;

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Null => null,
            _ => prop.ToString()
        };
    }

    private static int? ParseServings(JsonElement recipe)
    {
        if (!recipe.TryGetProperty("recipeYield", out var yield))
            return null;

        // Can be a string like "4", "4 personen", or a number
        var raw = yield.ValueKind == JsonValueKind.String
            ? yield.GetString()
            : yield.ToString();

        if (raw == null) return null;

        // Extract first numeric value from string
        var match = Regex.Match(raw, @"\d+");
        if (match.Success && int.TryParse(match.Value, out var servings))
            return servings;

        return null;
    }

    private static string? ExtractImageUrl(JsonElement recipe)
    {
        if (!recipe.TryGetProperty("image", out var image))
            return null;

        return image.ValueKind switch
        {
            // Single URL string
            JsonValueKind.String => image.GetString(),
            // Array — take first
            JsonValueKind.Array => image.EnumerateArray()
                .Select(img => img.ValueKind == JsonValueKind.String
                    ? img.GetString()
                    : GetStringFromElement(img, "url"))
                .FirstOrDefault(url => url != null),
            // ImageObject
            JsonValueKind.Object => GetStringFromElement(image, "url"),
            _ => null
        };
    }

    private static string? ExtractVideoUrl(JsonElement recipe)
    {
        if (!recipe.TryGetProperty("video", out var video))
            return null;

        if (video.ValueKind == JsonValueKind.Object)
        {
            return GetStringFromElement(video, "embedUrl")
                ?? GetStringFromElement(video, "url")
                ?? GetStringFromElement(video, "contentUrl");
        }

        return null;
    }

    private static List<ImportedIngredientDto> ExtractIngredients(JsonElement recipe)
    {
        if (!recipe.TryGetProperty("recipeIngredient", out var ingredientsEl))
            return [];

        if (ingredientsEl.ValueKind != JsonValueKind.Array)
            return [];

        return ingredientsEl.EnumerateArray()
            .Select((el, index) =>
            {
                var raw = el.GetString() ?? string.Empty;
                return new ImportedIngredientDto(
                    RawText: raw,
                    Name: raw,  // Phase 1: no parsing, store as-is
                    OriginalQuantity: null,
                    OriginalUnit: null
                );
            })
            .Where(i => !string.IsNullOrWhiteSpace(i.RawText))
            .ToList();
    }

    private static List<string> ExtractSteps(JsonElement recipe)
    {
        if (!recipe.TryGetProperty("recipeInstructions", out var instructions))
            return [];

        if (instructions.ValueKind == JsonValueKind.String)
            return [instructions.GetString() ?? string.Empty];

        if (instructions.ValueKind != JsonValueKind.Array)
            return [];

        return FlattenInstructions(instructions.EnumerateArray())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    /// <summary>
    /// Flattens instruction items that may be HowToStep (with "text") or
    /// HowToSection (with "itemListElement" containing nested HowToSteps).
    /// </summary>
    private static IEnumerable<string> FlattenInstructions(IEnumerable<JsonElement> items)
    {
        foreach (var item in items)
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                yield return item.GetString()!;
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
                continue;

            // HowToStep — has a direct "text" property
            var text = GetStringFromElement(item, "text");
            if (text != null)
            {
                yield return text;
                continue;
            }

            // HowToSection — has "itemListElement" containing nested steps
            if (item.TryGetProperty("itemListElement", out var nested) &&
                nested.ValueKind == JsonValueKind.Array)
            {
                foreach (var step in FlattenInstructions(nested.EnumerateArray()))
                    yield return step;
            }
        }
    }
}

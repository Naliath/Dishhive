using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace Dishhive.Api.Services.Import;

/// <summary>
/// Shared extraction engine: finds a schema.org Recipe object in the JSON-LD
/// (&lt;script type="application/ld+json"&gt;) blocks of an HTML page and maps it to an
/// <see cref="ImportedRecipe"/>. JSON-LD is the machine-intended surface of recipe pages,
/// which makes this the most stable extraction strategy (see docs/features/recipe-import.md).
/// </summary>
public static partial class SchemaOrgRecipeExtractor
{
    [GeneratedRegex("""<script[^>]*type\s*=\s*["']application/ld\+json["'][^>]*>(.*?)</script>""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex LdJsonScriptRegex();

    /// <summary>
    /// Attempts to extract a Recipe from the page. Returns null when no Recipe JSON-LD is present.
    /// </summary>
    public static ImportedRecipe? Extract(string html, Uri sourceUrl)
    {
        foreach (Match match in LdJsonScriptRegex().Matches(html))
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(match.Groups[1].Value);
            }
            catch (JsonException)
            {
                continue; // malformed block; keep looking
            }

            using (document)
            {
                var recipe = FindRecipeNode(document.RootElement);
                if (recipe.HasValue)
                {
                    return MapRecipe(recipe.Value, sourceUrl);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Collects every Recipe node in a JSON-LD document (single object, array, or @graph) —
    /// used by file import, where one file can carry a whole library.
    /// The returned elements are views into the caller's JsonDocument.
    /// </summary>
    public static List<JsonElement> FindRecipeNodes(JsonElement root)
    {
        var nodes = new List<JsonElement>();
        CollectRecipeNodes(root, nodes);
        return nodes;
    }

    /// <summary>
    /// Maps a single Recipe node without a backing page URL (file import).
    /// Returns null when the node has no name — there is nothing useful to import.
    /// </summary>
    public static ImportedRecipe? MapRecipeNode(JsonElement recipe)
    {
        return GetString(recipe, "name") == null ? null : MapRecipe(recipe, sourceUrl: null);
    }

    private static void CollectRecipeNodes(JsonElement element, List<JsonElement> nodes)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (HasType(element, "Recipe"))
                {
                    nodes.Add(element);
                }
                else if (element.TryGetProperty("@graph", out var graph))
                {
                    CollectRecipeNodes(graph, nodes);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectRecipeNodes(item, nodes);
                }
                break;
        }
    }

    private static JsonElement? FindRecipeNode(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (HasType(element, "Recipe"))
                {
                    return element;
                }
                // schema.org documents often nest nodes under @graph
                if (element.TryGetProperty("@graph", out var graph))
                {
                    return FindRecipeNode(graph);
                }
                return null;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var found = FindRecipeNode(item);
                    if (found.HasValue)
                    {
                        return found;
                    }
                }
                return null;

            default:
                return null;
        }
    }

    private static bool HasType(JsonElement element, string type)
    {
        if (!element.TryGetProperty("@type", out var typeProperty))
        {
            return false;
        }

        return typeProperty.ValueKind switch
        {
            JsonValueKind.String => string.Equals(typeProperty.GetString(), type, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => typeProperty.EnumerateArray()
                .Any(t => t.ValueKind == JsonValueKind.String
                       && string.Equals(t.GetString(), type, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private static ImportedRecipe MapRecipe(JsonElement recipe, Uri? sourceUrl)
    {
        var title = GetString(recipe, "name") ?? sourceUrl!.AbsoluteUri;

        return new ImportedRecipe
        {
            Title = title,
            Description = GetString(recipe, "description"),
            IngredientLines = GetStringList(recipe, "recipeIngredient"),
            Steps = GetInstructionTexts(recipe),
            Servings = GetServings(recipe),
            ImageUrl = GetImageUrl(recipe),
            VideoUrl = GetVideoUrl(recipe),
            SourceUrl = GetString(recipe, "@id") ?? GetString(recipe, "url") ?? sourceUrl?.AbsoluteUri,
            PrepTimeMinutes = GetDurationMinutes(recipe, "prepTime"),
            CookTimeMinutes = GetDurationMinutes(recipe, "cookTime"),
            TotalTimeMinutes = GetDurationMinutes(recipe, "totalTime"),
            Category = GetStringOrJoinedList(recipe, "recipeCategory"),
            Keywords = GetStringOrJoinedList(recipe, "keywords"),
            RawData = recipe.GetRawText()
        };
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        return null;
    }

    private static List<string> GetStringList(JsonElement element, string property)
    {
        var result = new List<string>();
        if (!element.TryGetProperty(property, out var value))
        {
            return result;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            result.Add(value.GetString()!.Trim());
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            result.AddRange(value.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString()!.Trim())
                .Where(v => v.Length > 0));
        }

        return result;
    }

    private static string? GetStringOrJoinedList(JsonElement element, string property)
    {
        var values = GetStringList(element, property);
        return values.Count > 0 ? string.Join(", ", values) : null;
    }

    private static List<string> GetInstructionTexts(JsonElement recipe)
    {
        var steps = new List<string>();
        if (!recipe.TryGetProperty("recipeInstructions", out var instructions))
        {
            return steps;
        }

        CollectInstructionTexts(instructions, steps);
        return steps;
    }

    private static void CollectInstructionTexts(JsonElement element, List<string> steps)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    steps.Add(text.Trim());
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectInstructionTexts(item, steps);
                }
                break;

            case JsonValueKind.Object:
                // HowToSection wraps steps in itemListElement; HowToStep carries text
                if (element.TryGetProperty("itemListElement", out var nested))
                {
                    CollectInstructionTexts(nested, steps);
                }
                else
                {
                    var stepText = GetString(element, "text");
                    if (stepText != null)
                    {
                        steps.Add(stepText);
                    }
                }
                break;
        }
    }

    private static int? GetServings(JsonElement recipe)
    {
        if (!recipe.TryGetProperty("recipeYield", out var yield))
        {
            return null;
        }

        return yield.ValueKind switch
        {
            JsonValueKind.Number => yield.TryGetInt32(out var number) ? number : null,
            JsonValueKind.String => ParseLeadingInt(yield.GetString()),
            JsonValueKind.Array => yield.EnumerateArray()
                .Select(v => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
                    ? n
                    : ParseLeadingInt(v.ValueKind == JsonValueKind.String ? v.GetString() : null))
                .FirstOrDefault(v => v.HasValue),
            _ => null
        };
    }

    private static int? ParseLeadingInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var digits = new string(text.Trim().TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : null;
    }

    private static string? GetImageUrl(JsonElement recipe)
    {
        if (!recipe.TryGetProperty("image", out var image))
        {
            return null;
        }

        return ExtractUrl(image);
    }

    private static string? GetVideoUrl(JsonElement recipe)
    {
        if (!recipe.TryGetProperty("video", out var video))
        {
            return null;
        }

        // VideoObject: prefer contentUrl, then embedUrl, then url
        if (video.ValueKind == JsonValueKind.Object)
        {
            return GetString(video, "contentUrl") ?? GetString(video, "embedUrl") ?? GetString(video, "url");
        }

        return ExtractUrl(video);
    }

    private static string? ExtractUrl(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Array => element.EnumerateArray().Select(ExtractUrl).FirstOrDefault(u => u != null),
            JsonValueKind.Object => GetString(element, "url") ?? GetString(element, "contentUrl"),
            _ => null
        };
    }

    private static int? GetDurationMinutes(JsonElement recipe, string property)
    {
        var iso = GetString(recipe, property);
        if (iso == null)
        {
            return null;
        }

        try
        {
            return (int)XmlConvert.ToTimeSpan(iso).TotalMinutes;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

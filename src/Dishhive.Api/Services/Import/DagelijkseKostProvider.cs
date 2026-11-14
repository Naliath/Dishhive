using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dishhive.Api.Services.Import;

/// <summary>
/// Recipe import provider for Dagelijkse Kost (https://dagelijksekost.vrt.be/).
///
/// Research outcome (June 2026, see docs/features/recipe-import.md): VRT publishes no formal
/// API for this site, but every recipe page embeds a schema.org Recipe JSON-LD block, which is
/// the machine-intended and most stable surface. Extraction therefore delegates to the shared
/// <see cref="SchemaOrgRecipeExtractor"/>.
///
/// Known site quirk: the JSON-LD recipeInstructions list is truncated (only the first two
/// steps are published there). The complete instruction list lives in the page's Next.js
/// flight payload (self.__next_f.push chunks) under recipeParts[].instructions[].description.
/// Older pages also expose an "instructions" map keyed by step index.
/// This provider extracts the full list and prefers it over the truncated JSON-LD steps,
/// falling back to JSON-LD when the payload cannot be parsed.
///
/// Second quirk: the JSON-LD "name" duplicates the SEO description sentence
/// ("Jeroen Meus maakt ..."), while the og:title meta tag carries the clean dish name
/// ("Lasagne verde"). The og:title is therefore preferred as recipe title.
/// </summary>
public partial class DagelijkseKostProvider : IRecipeSourceProvider
{
    [GeneratedRegex("""self\.__next_f\.push\(\[1,"((?:[^"\\]|\\.)*)"\]\)""", RegexOptions.Singleline)]
    private static partial Regex NextFlightChunkRegex();

    [GeneratedRegex("\"recipeParts\"\\s*:\\s*\\[")]
    private static partial Regex RecipePartsRegex();

    [GeneratedRegex(
        """<meta[^>]*property\s*=\s*["']og:title["'][^>]*content\s*=\s*["']([^"']+)["']|<meta[^>]*content\s*=\s*["']([^"']+)["'][^>]*property\s*=\s*["']og:title["']""",
        RegexOptions.IgnoreCase)]
    private static partial Regex OgTitleRegex();

    private const string InstructionsMarker = "\"instructions\":{\"0\":";

    public string Key => "dagelijkse-kost";

    public string DisplayName => "Dagelijkse Kost";

    public IReadOnlyList<string> Hosts => ["dagelijksekost.vrt.be"];

    public bool CanHandle(Uri url)
    {
        return url.Host.Equals("dagelijksekost.vrt.be", StringComparison.OrdinalIgnoreCase)
            || url.Host.Equals("www.dagelijksekost.vrt.be", StringComparison.OrdinalIgnoreCase);
    }

    public Task<ImportedRecipe> ExtractAsync(string html, Uri sourceUrl, CancellationToken cancellationToken = default)
    {
        var recipe = SchemaOrgRecipeExtractor.Extract(html, sourceUrl)
            ?? throw new RecipeExtractionFailedException(
                $"No schema.org Recipe JSON-LD found at '{sourceUrl}'. The page may not be a recipe, or the site layout changed.");

        // JSON-LD only carries the first steps; prefer the complete list from the page payload
        var fullSteps = ExtractStepsFromNextData(html);
        if (fullSteps != null && fullSteps.Count > recipe.Steps.Count)
        {
            recipe = recipe with { Steps = fullSteps };
        }

        // JSON-LD "name" is the SEO sentence; og:title is the clean dish name
        var ogTitle = ExtractOgTitle(html);
        if (ogTitle != null)
        {
            recipe = recipe with { Title = ogTitle };
        }

        return Task.FromResult(recipe);
    }

    private static string? ExtractOgTitle(string html)
    {
        var match = OgTitleRegex().Match(html);
        if (!match.Success)
        {
            return null;
        }

        var raw = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        var title = WebUtility.HtmlDecode(raw).Trim();
        return title.Length > 0 ? title : null;
    }

    /// <summary>
    /// Extracts the complete instruction list from the Next.js flight payload.
    /// Returns null when the payload is absent or its structure changed (JSON-LD fallback applies).
    /// </summary>
    private static IReadOnlyList<string>? ExtractStepsFromNextData(string html)
    {
        // The flight payload is split across script chunks, each a JS string literal;
        // decode and concatenate them so instruction data can't be cut by a chunk boundary
        var builder = new StringBuilder();
        foreach (Match match in NextFlightChunkRegex().Matches(html))
        {
            try
            {
                builder.Append(JsonSerializer.Deserialize<string>($"\"{match.Groups[1].Value}\""));
            }
            catch (JsonException)
            {
                // Chunk with non-JSON escaping; skip it
            }
        }

        var payload = builder.ToString();
        var partSteps = ExtractStepsFromRecipeParts(payload);
        if (partSteps != null)
        {
            return partSteps;
        }

        // Legacy pages expose a flattened map instead of (or alongside) recipeParts.
        var markerIndex = payload.IndexOf(InstructionsMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var objectStart = payload.IndexOf('{', markerIndex);
        var objectJson = SliceJsonContainer(payload, objectStart);
        if (objectJson == null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(objectJson);
            var steps = document.RootElement.EnumerateObject()
                .Where(p => int.TryParse(p.Name, out _) && p.Value.ValueKind == JsonValueKind.String)
                .OrderBy(p => int.Parse(p.Name))
                .Select(p => p.Value.GetString()!.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            return steps.Count > 0 ? steps : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string>? ExtractStepsFromRecipeParts(string payload)
    {
        foreach (Match match in RecipePartsRegex().Matches(payload))
        {
            var arrayJson = SliceJsonContainer(payload, match.Index + match.Length - 1);
            if (arrayJson == null)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(arrayJson);
                var steps = new List<string>();
                foreach (var part in document.RootElement.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object
                        || !part.TryGetProperty("instructions", out var instructions)
                        || instructions.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    // Parts and their instruction arrays carry the cooking order. Read only
                    // each instruction's description, excluding ingredient descriptions and tips.
                    foreach (var instruction in instructions.EnumerateArray())
                    {
                        if (instruction.ValueKind == JsonValueKind.Object
                            && instruction.TryGetProperty("description", out var description)
                            && description.ValueKind == JsonValueKind.String
                            && !string.IsNullOrWhiteSpace(description.GetString()))
                        {
                            steps.Add(description.GetString()!.Trim());
                        }
                    }
                }

                if (steps.Count > 0)
                {
                    return steps;
                }
            }
            catch (JsonException)
            {
                // Keep looking; the legacy map or JSON-LD can still be usable.
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the balanced JSON object or array starting at <paramref name="start"/>,
    /// or null when the text ends before the container closes.
    /// </summary>
    private static string? SliceJsonContainer(string text, int start)
    {
        var depth = 0;
        var inString = false;

        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (ch == '\\')
                {
                    i++; // skip the escaped character
                }
                else if (ch == '"')
                {
                    inString = false;
                }
            }
            else if (ch == '"')
            {
                inString = true;
            }
            else if (ch is '{' or '[')
            {
                depth++;
            }
            else if (ch is '}' or ']')
            {
                depth--;
                if (depth == 0)
                {
                    return text[start..(i + 1)];
                }
            }
        }

        return null;
    }
}

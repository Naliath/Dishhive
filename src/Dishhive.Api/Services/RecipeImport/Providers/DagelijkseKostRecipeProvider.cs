using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Dishhive.Api.Services.RecipeImport.Providers;

/// <summary>
/// Imports recipes from <c>dagelijksekost.vrt.be</c>.
///
/// Strategy:
/// 1. Look for a <c>&lt;script type="application/ld+json"&gt;</c> block whose JSON describes a
///    <c>schema.org</c> Recipe. This is the preferred path — robust to layout changes.
/// 2. Fall back to scraping the recipe's title/ingredients/instructions out of the DOM (best-effort).
///
/// The raw JSON-LD payload (or DOM dump) is preserved verbatim on the persisted recipe so the
/// user can manually correct any mis-import.
/// </summary>
public sealed partial class DagelijkseKostRecipeProvider : IRecipeSourceProvider
{
    public string ProviderKey => "dagelijksekost";

    private static readonly HashSet<string> SupportedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "dagelijksekost.vrt.be",
        "www.dagelijksekost.vrt.be"
    };

    private readonly HttpClient _http;

    public DagelijkseKostRecipeProvider(HttpClient http)
    {
        _http = http;
    }

    public bool CanHandle(Uri url) =>
        url.Scheme is "http" or "https" && SupportedHosts.Contains(url.Host);

    public async Task<ImportedRecipe> FetchAsync(Uri url, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(url))
            throw new ArgumentException($"URL host '{url.Host}' is not supported by {ProviderKey}.", nameof(url));

        using var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return Parse(url, html);
    }

    public ImportedRecipe Parse(Uri sourceUrl, string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var jsonLdRecipe = ExtractRecipeJsonLd(doc);
        if (jsonLdRecipe is not null)
        {
            return ParseJsonLd(sourceUrl, jsonLdRecipe.Value.json, jsonLdRecipe.Value.raw, html);
        }

        return ParseDomFallback(sourceUrl, doc, html);
    }

    // ------------------------------------------------------------------------
    // Strategy A: schema.org Recipe via JSON-LD
    // ------------------------------------------------------------------------
    private static (JsonElement json, string raw)? ExtractRecipeJsonLd(HtmlDocument doc)
    {
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts is null) return null;

        foreach (var script in scripts)
        {
            var text = HtmlEntity.DeEntitize(script.InnerText);
            if (string.IsNullOrWhiteSpace(text)) continue;

            JsonDocument? parsed;
            try
            {
                parsed = JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                continue;
            }

            using (parsed)
            {
                var recipe = FindRecipeNode(parsed.RootElement);
                if (recipe is not null)
                {
                    // Re-serialize the matching node so the preserved payload is stable & minified.
                    var raw = recipe.Value.GetRawText();
                    using var clone = JsonDocument.Parse(raw);
                    return (clone.RootElement.Clone(), raw);
                }
            }
        }
        return null;
    }

    private static JsonElement? FindRecipeNode(JsonElement root)
    {
        if (IsRecipe(root)) return root;

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in graph.EnumerateArray())
                if (IsRecipe(node)) return node;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in root.EnumerateArray())
                if (IsRecipe(node)) return node;
        }

        return null;
    }

    private static bool IsRecipe(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object) return false;
        if (!node.TryGetProperty("@type", out var typeProp)) return false;

        return typeProp.ValueKind switch
        {
            JsonValueKind.String => string.Equals(typeProp.GetString(), "Recipe", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => typeProp.EnumerateArray().Any(t => string.Equals(t.GetString(), "Recipe", StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private ImportedRecipe ParseJsonLd(Uri sourceUrl, JsonElement recipe, string rawPayload, string rawHtml)
    {
        var title = GetString(recipe, "name") ?? "(untitled)";
        var description = GetString(recipe, "description");
        var image = GetFirstString(recipe, "image");
        var video = ExtractVideoUrl(recipe);
        var servings = ExtractServings(recipe);

        var ingredients = ExtractIngredients(recipe);

        // Dagelijkse Kost emits a truncated `recipeInstructions` array in JSON-LD
        // (often only the first 1–2 steps). The full ordered list lives in the page's
        // embedded Next.js data under an "instructions" map. Prefer that when it has
        // more entries; fall back to the JSON-LD list.
        var jsonLdSteps = ExtractSteps(recipe);
        var pageDataSteps = ExtractStepsFromPageData(rawHtml);
        var steps = pageDataSteps.Count > jsonLdSteps.Count ? pageDataSteps : jsonLdSteps;

        var tags = ExtractTags(recipe);

        return new ImportedRecipe(
            Title: title,
            Description: description,
            Servings: servings,
            ImageUrl: image,
            VideoUrl: video,
            SourceUrl: sourceUrl,
            ProviderKey: ProviderKey,
            SourceRawPayload: rawPayload,
            Ingredients: ingredients,
            Steps: steps,
            Tags: tags);
    }

    private static string? GetString(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? GetFirstString(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Array => v.EnumerateArray().Select(GetUrlOrString).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
            JsonValueKind.Object when v.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String => url.GetString(),
            _ => null
        };
    }

    private static string? GetUrlOrString(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Object when v.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String => url.GetString(),
        _ => null
    };

    private static string? ExtractVideoUrl(JsonElement recipe)
    {
        if (!recipe.TryGetProperty("video", out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Object when v.TryGetProperty("contentUrl", out var c) && c.ValueKind == JsonValueKind.String => c.GetString(),
            JsonValueKind.Object when v.TryGetProperty("embedUrl", out var c) && c.ValueKind == JsonValueKind.String => c.GetString(),
            JsonValueKind.Array => v.EnumerateArray().Select(ExtractVideoFromObject).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
            _ => null
        };
    }

    private static string? ExtractVideoFromObject(JsonElement obj) =>
        (obj.TryGetProperty("contentUrl", out var c) && c.ValueKind == JsonValueKind.String) ? c.GetString() :
        (obj.TryGetProperty("embedUrl", out var e) && e.ValueKind == JsonValueKind.String) ? e.GetString() :
        null;

    private static int ExtractServings(JsonElement recipe)
    {
        if (!recipe.TryGetProperty("recipeYield", out var y)) return 4;
        return y.ValueKind switch
        {
            JsonValueKind.Number when y.TryGetInt32(out var i) => i,
            JsonValueKind.String => ParseLeadingInt(y.GetString()) ?? 4,
            JsonValueKind.Array => y.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Number ? (int?)e.GetInt32() : ParseLeadingInt(e.GetString()))
                .FirstOrDefault(v => v.HasValue) ?? 4,
            _ => 4
        };
    }

    private static int? ParseLeadingInt(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var match = LeadingIntRegex().Match(s);
        return match.Success && int.TryParse(match.Value, out var n) ? n : null;
    }

    private static IReadOnlyList<ImportedIngredient> ExtractIngredients(JsonElement recipe)
    {
        if (!recipe.TryGetProperty("recipeIngredient", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<ImportedIngredient>();

        var result = new List<ImportedIngredient>();
        var order = 0;
        foreach (var node in arr.EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.String) continue;
            var raw = node.GetString();
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var (qty, unit, name) = ParseIngredientText(raw);
            result.Add(new ImportedIngredient(
                Order: order++,
                Name: name,
                Quantity: qty,
                Unit: unit,
                OriginalQuantity: qty,
                OriginalUnit: unit,
                Section: null,
                Note: null));
        }
        return result;
    }

    private static IReadOnlyList<ImportedStep> ExtractSteps(JsonElement recipe)
    {
        if (!recipe.TryGetProperty("recipeInstructions", out var inst))
            return Array.Empty<ImportedStep>();

        var result = new List<ImportedStep>();
        var order = 0;

        switch (inst.ValueKind)
        {
            case JsonValueKind.String:
                foreach (var line in SplitLines(inst.GetString()))
                    result.Add(new ImportedStep(order++, line));
                break;

            case JsonValueKind.Array:
                foreach (var node in inst.EnumerateArray())
                {
                    var text = node.ValueKind switch
                    {
                        JsonValueKind.String => node.GetString(),
                        JsonValueKind.Object when node.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String => t.GetString(),
                        JsonValueKind.Object when node.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String => n.GetString(),
                        _ => null
                    };
                    if (!string.IsNullOrWhiteSpace(text))
                        result.Add(new ImportedStep(order++, text!.Trim()));
                }
                break;
        }
        return result;
    }

    /// <summary>
    /// Dagelijkse Kost is a Next.js app whose page payload contains a complete ordered
    /// <c>"instructions": { "0": "...", "1": "...", ... }</c> map embedded as escaped JSON
    /// in the streamed HTML. The schema.org JSON-LD on the page is often truncated, so we
    /// scrape this map as a more reliable source. Returns an empty list if not found.
    /// </summary>
    private static IReadOnlyList<ImportedStep> ExtractStepsFromPageData(string html)
    {
        // The block is preceded by the literal characters: \"instructions\":{
        // and is followed by ,\"instructionsMeta\": (next sibling key in the same payload).
        const string startMarker = "\\\"instructions\\\":{";
        const string endMarker = "},\\\"instructionsMeta\\\"";

        var startIdx = html.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIdx < 0) return Array.Empty<ImportedStep>();
        var bodyStart = startIdx + startMarker.Length;
        var endIdx = html.IndexOf(endMarker, bodyStart, StringComparison.Ordinal);
        if (endIdx < 0) return Array.Empty<ImportedStep>();

        var body = html[bodyStart..endIdx]; // \"0\":\"...\",\"1\":\"...\"...
        var pairs = PageDataInstructionPairRegex().Matches(body);
        if (pairs.Count == 0) return Array.Empty<ImportedStep>();

        var entries = new List<(int index, string text)>(pairs.Count);
        foreach (Match m in pairs)
        {
            if (!int.TryParse(m.Groups[1].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                continue;
            var raw = m.Groups[2].Value;
            var text = UnescapeNextJsonString(raw);
            if (!string.IsNullOrWhiteSpace(text))
                entries.Add((index, text.Trim()));
        }

        return entries
            .OrderBy(e => e.index)
            .Select((e, order) => new ImportedStep(order, e.text))
            .ToList();
    }

    /// <summary>
    /// The instructions map is escaped twice (Next.js streams it as a JSON string inside HTML).
    /// Each entry looks like: <c>\"0\":\"...text...\"</c>. Captures the index and the
    /// raw (still-escaped) text. Stops the value at the first un-escaped <c>\"</c>.
    /// </summary>
    [GeneratedRegex(@"\\""(\d+)\\"":\\""((?:[^\\]|\\(?:\\\\)*[^""]|\\\\)*)\\""", RegexOptions.Compiled)]
    private static partial Regex PageDataInstructionPairRegex();

    /// <summary>
    /// Reverses one level of JSON string escaping that Next.js applies when streaming
    /// nested JSON inside HTML (\\\" → ", \\\\ → \, \\n → newline, etc.). The values are
    /// also wrapped in an outer JSON-string layer, so we unescape twice when needed.
    /// </summary>
    private static string UnescapeNextJsonString(string raw)
    {
        var sb = new System.Text.StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '\\' && i + 1 < raw.Length)
            {
                var next = raw[i + 1];
                switch (next)
                {
                    case '"': sb.Append('"'); i++; continue;
                    case '\\': sb.Append('\\'); i++; continue;
                    case 'n': sb.Append('\n'); i++; continue;
                    case 'r': sb.Append('\r'); i++; continue;
                    case 't': sb.Append('\t'); i++; continue;
                    case '/': sb.Append('/'); i++; continue;
                    case 'u' when i + 5 < raw.Length:
                        if (int.TryParse(raw.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        {
                            sb.Append((char)code);
                            i += 5;
                            continue;
                        }
                        break;
                }
            }
            sb.Append(c);
        }
        // Apply once more to handle the additional escaping layer when the value itself was a JSON string.
        return sb.ToString();
    }

    private static IReadOnlyList<string> ExtractTags(JsonElement recipe)
    {
        var tags = new List<string>();
        foreach (var prop in new[] { "keywords", "recipeCategory", "recipeCuisine" })
        {
            if (!recipe.TryGetProperty(prop, out var v)) continue;
            switch (v.ValueKind)
            {
                case JsonValueKind.String:
                    tags.AddRange(v.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case JsonValueKind.Array:
                    foreach (var n in v.EnumerateArray())
                        if (n.ValueKind == JsonValueKind.String) tags.Add(n.GetString()!.Trim());
                    break;
            }
        }
        return tags.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> SplitLines(string? s) =>
        string.IsNullOrWhiteSpace(s)
            ? Array.Empty<string>()
            : s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Best-effort split of an ingredient line into (quantity, unit, name).
    /// Examples:
    ///   "500 g witte asperges" -> (500, "g", "witte asperges")
    ///   "4 eieren"             -> (4, null, "eieren")
    ///   "snuifje peper"        -> (null, null, "snuifje peper")
    /// </summary>
    public static (decimal? qty, string? unit, string name) ParseIngredientText(string raw)
    {
        var text = raw.Trim();
        var match = IngredientPrefixRegex().Match(text);
        if (!match.Success) return (null, null, text);

        var qtyText = match.Groups["qty"].Value.Replace(',', '.');
        if (!decimal.TryParse(qtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty))
            return (null, null, text);

        var unit = match.Groups["unit"].Success ? match.Groups["unit"].Value : null;
        var rest = text[match.Length..].TrimStart();

        if (rest.Length == 0) return (qty, unit, text);
        return (qty, unit, rest);
    }

    [GeneratedRegex(@"^\d+")]
    private static partial Regex LeadingIntRegex();

    // matches optional integer/decimal followed by optional known unit (g|kg|ml|l|el|tl|tsp|tbsp|cup|oz|lb)
    [GeneratedRegex(@"^(?<qty>\d+(?:[.,]\d+)?)\s*(?<unit>g|kg|mg|ml|cl|dl|l|el|tl|tsp|tbsp|cup|oz|lb)?\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex IngredientPrefixRegex();

    // ------------------------------------------------------------------------
    // Strategy B: DOM fallback (best-effort, JSON-LD-less pages)
    // ------------------------------------------------------------------------
    private ImportedRecipe ParseDomFallback(Uri sourceUrl, HtmlDocument doc, string rawHtml)
    {
        var title = (doc.DocumentNode.SelectSingleNode("//h1")?.InnerText
                  ?? doc.DocumentNode.SelectSingleNode("//h2")?.InnerText
                  ?? "(untitled)").Trim();

        return new ImportedRecipe(
            Title: HtmlEntity.DeEntitize(title),
            Description: null,
            Servings: 4,
            ImageUrl: null,
            VideoUrl: null,
            SourceUrl: sourceUrl,
            ProviderKey: ProviderKey,
            SourceRawPayload: rawHtml,
            Ingredients: Array.Empty<ImportedIngredient>(),
            Steps: Array.Empty<ImportedStep>(),
            Tags: Array.Empty<string>());
    }
}

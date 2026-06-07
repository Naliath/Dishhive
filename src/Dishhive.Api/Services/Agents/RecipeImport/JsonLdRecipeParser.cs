using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dishhive.Api.Services.RecipeImport;
using HtmlAgilityPack;

namespace Dishhive.Api.Services.Agents.RecipeImport;

/// <summary>
/// Reusable schema.org Recipe (JSON-LD) parser. Handles the common shapes:
/// single object, <c>@graph</c> array, top-level array.
///
/// This is a focused subset; the per-host providers (e.g. <c>DagelijkseKostRecipeProvider</c>)
/// keep their own slightly-customized variants for site-specific quirks. The
/// <c>LearnedRecipeSourceProvider</c> uses this generic parser for any host whose
/// blueprint is <see cref="Models.Agents.LearnedRecipeSourceStrategy.JsonLd"/>.
/// </summary>
public static partial class JsonLdRecipeParser
{
    /// <summary>
    /// Extract an <see cref="ImportedRecipe"/> from a page's HTML. Returns <c>null</c>
    /// when no schema.org Recipe is present.
    /// </summary>
    public static ImportedRecipe? TryParse(Uri sourceUrl, string html, string providerKey)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return TryParse(sourceUrl, doc, providerKey);
    }

    public static ImportedRecipe? TryParse(Uri sourceUrl, HtmlDocument doc, string providerKey)
    {
        var (recipeJson, raw) = ExtractRecipeJsonLd(doc);
        if (recipeJson is null || raw is null) return null;

        var title = GetString(recipeJson.Value, "name") ?? "(untitled)";
        var description = GetString(recipeJson.Value, "description");
        var image = GetFirstString(recipeJson.Value, "image");
        var servings = ExtractServings(recipeJson.Value);

        return new ImportedRecipe(
            Title: title,
            Description: description,
            Servings: servings,
            ImageUrl: image,
            VideoUrl: ExtractVideo(recipeJson.Value),
            SourceUrl: sourceUrl,
            ProviderKey: providerKey,
            SourceRawPayload: raw,
            Ingredients: ExtractIngredients(recipeJson.Value),
            Steps: ExtractSteps(recipeJson.Value),
            Tags: ExtractTags(recipeJson.Value));
    }

    // ------------------------------------------------------------------------

    private static (JsonElement? json, string? raw) ExtractRecipeJsonLd(HtmlDocument doc)
    {
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts is null) return (null, null);

        foreach (var script in scripts)
        {
            var text = HtmlEntity.DeEntitize(script.InnerText);
            if (string.IsNullOrWhiteSpace(text)) continue;

            JsonDocument parsed;
            try { parsed = JsonDocument.Parse(text); }
            catch (JsonException) { continue; }

            using (parsed)
            {
                var node = FindRecipeNode(parsed.RootElement);
                if (node is null) continue;

                var raw = node.Value.GetRawText();
                using var clone = JsonDocument.Parse(raw);
                return (clone.RootElement.Clone(), raw);
            }
        }
        return (null, null);
    }

    private static JsonElement? FindRecipeNode(JsonElement root)
    {
        if (IsRecipe(root)) return root;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
            foreach (var n in graph.EnumerateArray())
                if (IsRecipe(n)) return n;
        if (root.ValueKind == JsonValueKind.Array)
            foreach (var n in root.EnumerateArray())
                if (IsRecipe(n)) return n;
        return null;
    }

    private static bool IsRecipe(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object) return false;
        if (!node.TryGetProperty("@type", out var t)) return false;
        return t.ValueKind switch
        {
            JsonValueKind.String => string.Equals(t.GetString(), "Recipe", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => t.EnumerateArray().Any(x => string.Equals(x.GetString(), "Recipe", StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private static string? GetString(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? GetFirstString(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Array => v.EnumerateArray().Select(UrlOrString).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
            JsonValueKind.Object when v.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String => u.GetString(),
            _ => null
        };
    }

    private static string? UrlOrString(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Object when v.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String => u.GetString(),
        _ => null
    };

    private static string? ExtractVideo(JsonElement r)
    {
        if (!r.TryGetProperty("video", out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Object => VideoFromObject(v),
            JsonValueKind.Array => v.EnumerateArray().Select(VideoFromObject).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
            _ => null
        };
    }

    private static string? VideoFromObject(JsonElement o) =>
        (o.TryGetProperty("contentUrl", out var c) && c.ValueKind == JsonValueKind.String) ? c.GetString() :
        (o.TryGetProperty("embedUrl", out var e) && e.ValueKind == JsonValueKind.String) ? e.GetString() :
        null;

    private static int ExtractServings(JsonElement r)
    {
        if (!r.TryGetProperty("recipeYield", out var y)) return 4;
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
        var m = LeadingIntRegex().Match(s);
        return m.Success && int.TryParse(m.Value, out var n) ? n : null;
    }

    private static IReadOnlyList<ImportedIngredient> ExtractIngredients(JsonElement r)
    {
        if (!r.TryGetProperty("recipeIngredient", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<ImportedIngredient>();

        var list = new List<ImportedIngredient>();
        var i = 0;
        foreach (var n in arr.EnumerateArray())
        {
            if (n.ValueKind != JsonValueKind.String) continue;
            var raw = n.GetString();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var (qty, unit, name) = ParseIngredientLine(raw);
            list.Add(new ImportedIngredient(i++, name, qty, unit, qty, unit, null, null));
        }
        return list;
    }

    private static IReadOnlyList<ImportedStep> ExtractSteps(JsonElement r)
    {
        if (!r.TryGetProperty("recipeInstructions", out var inst))
            return Array.Empty<ImportedStep>();

        var list = new List<ImportedStep>();
        var i = 0;
        switch (inst.ValueKind)
        {
            case JsonValueKind.String:
                foreach (var line in (inst.GetString() ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    list.Add(new ImportedStep(i++, line));
                break;
            case JsonValueKind.Array:
                foreach (var n in inst.EnumerateArray())
                {
                    var text = n.ValueKind switch
                    {
                        JsonValueKind.String => n.GetString(),
                        JsonValueKind.Object when n.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String => t.GetString(),
                        JsonValueKind.Object when n.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String => nm.GetString(),
                        _ => null
                    };
                    if (!string.IsNullOrWhiteSpace(text))
                        list.Add(new ImportedStep(i++, text!.Trim()));
                }
                break;
        }
        return list;
    }

    private static IReadOnlyList<string> ExtractTags(JsonElement r)
    {
        var tags = new List<string>();
        foreach (var prop in new[] { "keywords", "recipeCategory", "recipeCuisine" })
        {
            if (!r.TryGetProperty(prop, out var v)) continue;
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
        return tags.Where(t => !string.IsNullOrWhiteSpace(t))
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToList();
    }

    private static (decimal? qty, string? unit, string name) ParseIngredientLine(string raw)
    {
        var text = raw.Trim();
        var match = IngredientPrefixRegex().Match(text);
        if (!match.Success) return (null, null, text);
        var qtyText = match.Groups["qty"].Value.Replace(',', '.');
        if (!decimal.TryParse(qtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty))
            return (null, null, text);
        var unit = match.Groups["unit"].Success ? match.Groups["unit"].Value : null;
        var rest = text[match.Length..].TrimStart();
        return (qty, unit, rest.Length == 0 ? text : rest);
    }

    [GeneratedRegex(@"^\d+")]
    private static partial Regex LeadingIntRegex();

    [GeneratedRegex(@"^(?<qty>\d+(?:[.,]\d+)?)\s*(?<unit>g|kg|mg|ml|cl|dl|l|el|tl|tsp|tbsp|cup|oz|lb)?\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex IngredientPrefixRegex();
}

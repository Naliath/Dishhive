using Dishhive.Api.Models.Agents;
using Dishhive.Api.Services.RecipeImport;
using HtmlAgilityPack;

namespace Dishhive.Api.Services.Agents.RecipeImport;

/// <summary>
/// Replays an XPath-based <see cref="RecipeImportBlueprint"/> against fetched HTML.
/// Pure parser — no I/O.
/// </summary>
public static class XPathRecipeParser
{
    public static ImportedRecipe? TryParse(Uri sourceUrl, string html, string providerKey, RecipeImportBlueprint blueprint)
    {
        if (blueprint.Strategy != LearnedRecipeSourceStrategy.XPath) return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return TryParse(sourceUrl, doc, providerKey, blueprint, html);
    }

    public static ImportedRecipe? TryParse(Uri sourceUrl, HtmlDocument doc, string providerKey, RecipeImportBlueprint blueprint, string rawPayload)
    {
        var title = SelectSingle(doc, blueprint.TitleXPath);
        if (string.IsNullOrWhiteSpace(title)) return null;

        var description = SelectSingle(doc, blueprint.DescriptionXPath);
        var image = SelectAttrOrText(doc, blueprint.ImageXPath, "src");
        var servingsText = SelectSingle(doc, blueprint.ServingsXPath);
        var servings = ParseFirstInt(servingsText) ?? 4;

        var ingredients = SelectMany(doc, blueprint.IngredientsXPath)
            .Select((line, idx) => new ImportedIngredient(idx, line, null, null, null, null, null, null))
            .ToList();
        if (ingredients.Count == 0) return null;

        var steps = SelectMany(doc, blueprint.StepsXPath)
            .Select((line, idx) => new ImportedStep(idx, line))
            .ToList();

        return new ImportedRecipe(
            Title: title!,
            Description: description,
            Servings: servings,
            ImageUrl: image,
            VideoUrl: null,
            SourceUrl: sourceUrl,
            ProviderKey: providerKey,
            SourceRawPayload: rawPayload,
            Ingredients: ingredients,
            Steps: steps,
            Tags: Array.Empty<string>());
    }

    private static string? SelectSingle(HtmlDocument doc, string? xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath)) return null;
        var node = doc.DocumentNode.SelectSingleNode(xpath);
        return node is null ? null : HtmlEntity.DeEntitize(node.InnerText.Trim());
    }

    private static string? SelectAttrOrText(HtmlDocument doc, string? xpath, string attr)
    {
        if (string.IsNullOrWhiteSpace(xpath)) return null;
        var node = doc.DocumentNode.SelectSingleNode(xpath);
        if (node is null) return null;
        var attrValue = node.GetAttributeValue(attr, null);
        return !string.IsNullOrWhiteSpace(attrValue) ? attrValue : HtmlEntity.DeEntitize(node.InnerText.Trim());
    }

    private static IEnumerable<string> SelectMany(HtmlDocument doc, string? xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath)) return Array.Empty<string>();
        var nodes = doc.DocumentNode.SelectNodes(xpath);
        return nodes is null
            ? Array.Empty<string>()
            : nodes.Select(n => HtmlEntity.DeEntitize(n.InnerText).Trim())
                   .Where(s => !string.IsNullOrWhiteSpace(s))
                   .ToList();
    }

    private static int? ParseFirstInt(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(s, @"\d+");
        return match.Success && int.TryParse(match.Value, out var n) ? n : null;
    }
}

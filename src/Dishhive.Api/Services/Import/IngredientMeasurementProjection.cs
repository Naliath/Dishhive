using Dishhive.Api.Models;

namespace Dishhive.Api.Services.Import;

/// <summary>Repairs recognizable legacy import representations on read without changing source data.</summary>
public static class IngredientMeasurementProjection
{
    public static ParsedIngredient Resolve(RecipeIngredient ingredient, string? sourceRawData = null)
    {
        var source = IngredientLineParser.Parse(ingredient.OriginalText);
        var unparsed = ingredient.Quantity is null && ingredient.Unit is null
            && ingredient.Name.Trim() == ingredient.OriginalText.Trim();
        var legacyCup = ingredient.Unit == "ml"
            && sourceRawData?.Contains("\"metricIngredients\"", StringComparison.Ordinal) != true
            && ingredient.OriginalUnit?.ToLowerInvariant() is "cup" or "cups"
            && ingredient.OriginalQuantity is { } original
            && ingredient.Quantity == Math.Round(original * 240, 2)
            && source.Unit == "cup" && source.Quantity == original;
        if (source.Quantity is not null && (unparsed || legacyCup))
            return source with { Name = legacyCup ? ingredient.Name : source.Name };
        return new ParsedIngredient
        {
            Name = ingredient.Name, Quantity = ingredient.Quantity, Unit = ingredient.Unit,
            OriginalText = ingredient.OriginalText, OriginalQuantity = ingredient.OriginalQuantity,
            OriginalUnit = ingredient.OriginalUnit
        };
    }
}

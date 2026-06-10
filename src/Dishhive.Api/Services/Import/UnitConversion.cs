namespace Dishhive.Api.Services.Import;

/// <summary>
/// Normalizes measurement units to Dishhive's canonical metric units.
/// Storage is always metric; the user's measurement preference is a display concern
/// (see docs/features/measurement-preferences.md). Culinary units without a system
/// (spoons, leaves, pinches) pass through unchanged — converting those is noise.
/// </summary>
public static class UnitConversion
{
    private sealed record UnitInfo(string CanonicalUnit, decimal Factor);

    private static readonly Dictionary<string, UnitInfo> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        // Metric mass
        ["g"] = new("g", 1m),
        ["gr"] = new("g", 1m),
        ["gram"] = new("g", 1m),
        ["grams"] = new("g", 1m),
        ["kg"] = new("kg", 1m),
        ["kilo"] = new("kg", 1m),
        ["kilogram"] = new("kg", 1m),

        // Metric volume
        ["ml"] = new("ml", 1m),
        ["milliliter"] = new("ml", 1m),
        ["milliliters"] = new("ml", 1m),
        ["cl"] = new("ml", 10m),
        ["centiliter"] = new("ml", 10m),
        ["dl"] = new("ml", 100m),
        ["deciliter"] = new("ml", 100m),
        ["l"] = new("l", 1m),
        ["liter"] = new("l", 1m),
        ["liters"] = new("l", 1m),

        // Imperial mass → metric
        ["oz"] = new("g", 28.35m),
        ["ounce"] = new("g", 28.35m),
        ["ounces"] = new("g", 28.35m),
        ["lb"] = new("g", 453.59m),
        ["lbs"] = new("g", 453.59m),
        ["pound"] = new("g", 453.59m),
        ["pounds"] = new("g", 453.59m),

        // Imperial volume → metric (volume only; no density guessing)
        ["cup"] = new("ml", 240m),
        ["cups"] = new("ml", 240m),
        ["pint"] = new("ml", 473m),
        ["pints"] = new("ml", 473m),

        // Culinary units — pass through (Dutch and English spellings)
        ["el"] = new("el", 1m),
        ["eetlepel"] = new("el", 1m),
        ["eetlepels"] = new("el", 1m),
        ["tbsp"] = new("el", 1m),
        ["tablespoon"] = new("el", 1m),
        ["tablespoons"] = new("el", 1m),
        ["tl"] = new("tl", 1m),
        ["theelepel"] = new("tl", 1m),
        ["theelepels"] = new("tl", 1m),
        ["tsp"] = new("tl", 1m),
        ["teaspoon"] = new("tl", 1m),
        ["teaspoons"] = new("tl", 1m),
        ["snuifje"] = new("snuifje", 1m),
        ["pinch"] = new("snuifje", 1m),
        ["blaadje"] = new("blaadje", 1m),
        ["blaadjes"] = new("blaadje", 1m),
        ["teentje"] = new("teentje", 1m),
        ["teentjes"] = new("teentje", 1m),
        ["takje"] = new("takje", 1m),
        ["takjes"] = new("takje", 1m),
        ["bakje"] = new("bakje", 1m),
        ["bakjes"] = new("bakje", 1m),
        ["stuk"] = new("piece", 1m),
        ["stuks"] = new("piece", 1m),
        ["stukken"] = new("piece", 1m),
    };

    /// <summary>
    /// Tries to normalize a unit token. Returns false when the token is not a known unit
    /// (the caller then treats it as part of the ingredient name).
    /// </summary>
    public static bool TryNormalize(string unitToken, decimal quantity, out string canonicalUnit, out decimal normalizedQuantity)
    {
        if (Units.TryGetValue(unitToken.Trim(), out var info))
        {
            canonicalUnit = info.CanonicalUnit;
            normalizedQuantity = Math.Round(quantity * info.Factor, 2);
            return true;
        }

        canonicalUnit = string.Empty;
        normalizedQuantity = quantity;
        return false;
    }
}

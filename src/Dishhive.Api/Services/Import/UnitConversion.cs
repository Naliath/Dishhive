using Dishhive.Api.Services.Localization;

namespace Dishhive.Api.Services.Import;

/// <summary>
/// Normalizes units through per-language resource files into canonical storage units.
/// Source-language parsing is independent of the user's selected display language.
/// </summary>
public static class UnitConversion
{
    public static bool TryNormalize(
        string unitToken, decimal quantity, out string canonicalUnit, out decimal normalizedQuantity)
    {
        if (LocalizationLexicon.Units.TryGetValue(unitToken.Trim(), out var info))
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

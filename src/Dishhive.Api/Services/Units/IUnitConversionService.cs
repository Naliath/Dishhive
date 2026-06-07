using Dishhive.Api.Models.Settings;

namespace Dishhive.Api.Services.Units;

/// <summary>
/// Converts between metric and imperial cooking units. Conversions are deliberately
/// conservative — when a unit is ambiguous (e.g. "cup" → grams without ingredient density)
/// we leave the value untouched. Original values are preserved on the entity.
/// </summary>
public interface IUnitConversionService
{
    /// <summary>
    /// Converts <paramref name="qty"/> in <paramref name="unit"/> to the user's preferred
    /// <paramref name="target"/> measurement system. Returns the input unchanged when no safe
    /// conversion is known.
    /// </summary>
    (decimal qty, string unit) ConvertForSystem(decimal qty, string unit, MeasurementSystem target);
}

public sealed class UnitConversionService : IUnitConversionService
{
    // Conservative conversion table. Source unit (lowercased) → (factor, target unit, target system).
    private static readonly Dictionary<string, (decimal factor, string toMetric, string toImperial)> Table =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // mass
            ["g"] = (1m / 28.3495m, "g", "oz"),
            ["kg"] = (2.20462m, "kg", "lb"),
            ["oz"] = (28.3495m, "g", "oz"),
            ["lb"] = (0.453592m, "kg", "lb"),
            // volume (treats fl oz as US fluid ounce)
            ["ml"] = (1m / 29.5735m, "ml", "fl oz"),
            ["l"] = (4.22675m, "l", "cup"),
            ["fl oz"] = (29.5735m, "ml", "fl oz"),
            ["cup"] = (236.588m, "ml", "cup"),
        };

    public (decimal qty, string unit) ConvertForSystem(decimal qty, string unit, MeasurementSystem target)
    {
        var key = unit.Trim().ToLowerInvariant();
        if (!Table.TryGetValue(key, out var entry)) return (qty, unit);

        var canonicalUnit = target == MeasurementSystem.Metric ? entry.toMetric : entry.toImperial;
        if (string.Equals(canonicalUnit, key, StringComparison.OrdinalIgnoreCase))
            return (qty, unit);

        // Convert when we're crossing systems. The factor is "to the OTHER system".
        var converted = qty * entry.factor;
        return (Math.Round(converted, 3), canonicalUnit);
    }
}

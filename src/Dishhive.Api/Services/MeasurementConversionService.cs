namespace Dishhive.Api.Services;

/// <summary>
/// Converts ingredient quantities between metric and imperial units.
/// </summary>
public interface IMeasurementConversionService
{
    /// <summary>
    /// Convert a quantity from one unit to another.
    /// Returns (quantity, unit) in the target system, or the originals if no conversion is defined.
    /// </summary>
    (decimal? quantity, string? unit) Convert(decimal? quantity, string? unit, string targetSystem);

    /// <summary>Format a quantity + unit pair for display.</summary>
    string Format(decimal? quantity, string? unit, string measurementSystem);
}

public class MeasurementConversionService : IMeasurementConversionService
{
    // Metric → Imperial conversions
    private static readonly Dictionary<string, (string unit, decimal factor)> MetricToImperial = new(StringComparer.OrdinalIgnoreCase)
    {
        ["g"]   = ("oz",  0.035274m),
        ["kg"]  = ("lb",  2.20462m),
        ["ml"]  = ("fl oz", 0.033814m),
        ["l"]   = ("cups", 4.22675m),
        ["cm"]  = ("in",  0.393701m),
    };

    // Imperial → Metric conversions
    private static readonly Dictionary<string, (string unit, decimal factor)> ImperialToMetric = new(StringComparer.OrdinalIgnoreCase)
    {
        ["oz"]    = ("g",  28.3495m),
        ["lb"]    = ("kg", 0.453592m),
        ["fl oz"] = ("ml", 29.5735m),
        ["cup"]   = ("ml", 236.588m),
        ["cups"]  = ("ml", 236.588m),
        ["in"]    = ("cm", 2.54m),
        ["inch"]  = ("cm", 2.54m),
    };

    public (decimal? quantity, string? unit) Convert(decimal? quantity, string? unit, string targetSystem)
    {
        if (quantity == null || unit == null)
            return (quantity, unit);

        if (targetSystem == "imperial" && MetricToImperial.TryGetValue(unit, out var imp))
            return (Math.Round(quantity.Value * imp.factor, 2), imp.unit);

        if (targetSystem == "metric" && ImperialToMetric.TryGetValue(unit, out var met))
            return (Math.Round(quantity.Value * met.factor, 2), met.unit);

        return (quantity, unit);
    }

    public string Format(decimal? quantity, string? unit, string measurementSystem)
    {
        var (q, u) = Convert(quantity, unit, measurementSystem);

        if (q == null && u == null) return string.Empty;
        if (q == null) return u ?? string.Empty;
        if (u == null) return q.Value.ToString("0.##");

        return $"{q.Value:0.##} {u}";
    }
}

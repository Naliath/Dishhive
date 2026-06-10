using System.Globalization;
using System.Text.RegularExpressions;

namespace Dishhive.Api.Services.Import;

/// <summary>
/// A parsed ingredient line: normalized values for Dishhive plus the verbatim original
/// </summary>
public record ParsedIngredient
{
    public required string Name { get; init; }
    public decimal? Quantity { get; init; }
    public string? Unit { get; init; }
    public required string OriginalText { get; init; }
    public decimal? OriginalQuantity { get; init; }
    public string? OriginalUnit { get; init; }
}

/// <summary>
/// Best-effort parser for source ingredient lines such as "200 gram suiker",
/// "0,5 citroenen" (decimal comma) or "Cointreau" (no quantity).
/// Unparseable lines degrade gracefully: quantity/unit stay null and the full line
/// becomes the name. The original text is always preserved verbatim.
/// </summary>
public static partial class IngredientLineParser
{
    [GeneratedRegex(@"^(?<qty>\d+(?:[.,]\d+)?)\s+(?<rest>.+)$")]
    private static partial Regex QuantityLineRegex();

    public static ParsedIngredient Parse(string line)
    {
        var trimmed = line.Trim();
        var match = QuantityLineRegex().Match(trimmed);

        if (!match.Success)
        {
            // No leading quantity ("Cointreau", "peper en zout")
            return new ParsedIngredient { Name = trimmed, OriginalText = trimmed };
        }

        var quantity = decimal.Parse(
            match.Groups["qty"].Value.Replace(',', '.'),
            CultureInfo.InvariantCulture);

        var rest = match.Groups["rest"].Value.Trim();
        var spaceIndex = rest.IndexOf(' ');
        var firstToken = spaceIndex < 0 ? rest : rest[..spaceIndex];
        var remainder = spaceIndex < 0 ? string.Empty : rest[(spaceIndex + 1)..].Trim();

        // "200 gram suiker" → unit token + ingredient name
        if (remainder.Length > 0 && UnitConversion.TryNormalize(firstToken, quantity, out var canonicalUnit, out var normalized))
        {
            return new ParsedIngredient
            {
                Name = remainder,
                Quantity = normalized,
                Unit = canonicalUnit,
                OriginalText = trimmed,
                OriginalQuantity = quantity,
                OriginalUnit = firstToken
            };
        }

        // "3 eierdooiers", "0,5 citroenen" → countable pieces
        return new ParsedIngredient
        {
            Name = rest,
            Quantity = quantity,
            Unit = "piece",
            OriginalText = trimmed,
            OriginalQuantity = quantity,
            OriginalUnit = null
        };
    }
}

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
    [GeneratedRegex(@"^(?<qty>(?:\d+\s+)?\d+\s*[/⁄]\s*\d+|\d*[¼½¾⅐⅑⅒⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞]|\d+(?:[.,]\d+)?)\s*(?<rest>[^\d\s/⁄.,–—-].*)$")]
    private static partial Regex QuantityLineRegex();

    private static readonly IReadOnlyDictionary<char, decimal> Fractions = new Dictionary<char, decimal>
    {
        ['¼'] = 1m / 4, ['½'] = 1m / 2, ['¾'] = 3m / 4,
        ['⅐'] = 1m / 7, ['⅑'] = 1m / 9, ['⅒'] = 1m / 10,
        ['⅓'] = 1m / 3, ['⅔'] = 2m / 3, ['⅕'] = 1m / 5, ['⅖'] = 2m / 5,
        ['⅗'] = 3m / 5, ['⅘'] = 4m / 5, ['⅙'] = 1m / 6, ['⅚'] = 5m / 6,
        ['⅛'] = 1m / 8, ['⅜'] = 3m / 8, ['⅝'] = 5m / 8, ['⅞'] = 7m / 8
    };

    public static ParsedIngredient Parse(string line)
    {
        var trimmed = line.Trim();
        // A space between a whole number and a Unicode fraction is also a mixed number.
        var numericLine = Regex.Replace(trimmed, @"^(\d+)\s+([¼½¾⅐⅑⅒⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞])", "$1$2");
        var match = QuantityLineRegex().Match(numericLine);

        if (!match.Success || !TryParseQuantity(match.Groups["qty"].Value, out var quantity))
        {
            // No leading quantity ("Cointreau", "peper en zout")
            return new ParsedIngredient { Name = trimmed, OriginalText = trimmed };
        }

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

    private static bool TryParseQuantity(string text, out decimal quantity)
    {
        quantity = 0;
        if (Fractions.TryGetValue(text[^1], out var fraction))
        {
            if (text.Length > 1 && !decimal.TryParse(text[..^1], NumberStyles.None,
                    CultureInfo.InvariantCulture, out quantity)) return false;
            quantity += fraction;
            return true;
        }
        var parts = text.Replace('⁄', '/').Split('/');
        if (parts.Length == 1)
            return decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out quantity);
        var wholeAndNumerator = parts[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (wholeAndNumerator.Length is < 1 or > 2
            || !decimal.TryParse(wholeAndNumerator[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var numerator)
            || !decimal.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var denominator)
            || denominator == 0) return false;
        if (wholeAndNumerator.Length == 2 && !decimal.TryParse(wholeAndNumerator[0],
                NumberStyles.None, CultureInfo.InvariantCulture, out quantity)) return false;
        quantity += numerator / denominator;
        return true;
    }
}

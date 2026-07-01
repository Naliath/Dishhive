using System.Net;
using System.Text.RegularExpressions;

namespace Dishhive.Api.Services.Import;

/// <summary>
/// Cheap HTML → readable-text reduction (no HTML-parser dependency) for feeding a
/// page to the LLM when the structured scrapers can't parse it. Drops script/style/
/// nav/svg noise, unwraps tags to whitespace, decodes entities and caps the length
/// so a large page can't blow the model's context.
/// </summary>
public static partial class HtmlText
{
    [GeneratedRegex(@"<(script|style|noscript|svg|head|nav|footer|header|form)[^>]*>.*?</\1>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex NoiseBlockRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"[ \t\f\v]+")]
    private static partial Regex InlineSpaceRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLinesRegex();

    /// <summary>Converts page HTML to plain text, truncated to <paramref name="maxChars"/></summary>
    public static string ToPlainText(string html, int maxChars = 12000)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }

        var text = NoiseBlockRegex().Replace(html, " ");
        // Turn block-ish tags into line breaks so structure (ingredients, steps) survives
        text = Regex.Replace(text, @"</(p|div|li|tr|h[1-6]|br)\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = TagRegex().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);

        text = InlineSpaceRegex().Replace(text, " ");
        text = BlankLinesRegex().Replace(text, "\n\n");
        text = string.Join('\n', text.Split('\n').Select(l => l.Trim())).Trim();

        return text.Length > maxChars ? text[..maxChars] : text;
    }
}

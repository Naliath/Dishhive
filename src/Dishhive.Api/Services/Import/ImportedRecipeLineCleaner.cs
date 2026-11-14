namespace Dishhive.Api.Services.Import;

/// <summary>Removes source list decorations before imported lines are parsed or stored.</summary>
internal static class ImportedRecipeLineCleaner
{
    // Extend this collection for additional source list symbols. Only prefixes are removed.
    private static readonly char[] LeadingListMarkers =
    [
        '•', '◦', '‣', '⁃', '∙', '·', '●', '○', // Round bullets
        '■', '□', '▪', '▫', '▢',               // Square bullets
        '☐', '☑', '☒', '✓', '✔',              // Checkboxes and check marks
        '-', '+', '*', '–', '—'                // Plain-text bullets
    ];

    // These characters also occur in signs, ranges and emphasis, so require a separator.
    private static readonly char[] WhitespaceDelimitedListMarkers = ['-', '+', '*', '–', '—'];

    public static string Clean(string line)
    {
        var cleaned = line.AsSpan().Trim();
        while (!cleaned.IsEmpty && LeadingListMarkers.Contains(cleaned[0]))
        {
            if (WhitespaceDelimitedListMarkers.Contains(cleaned[0])
                && cleaned.Length > 1 && !char.IsWhiteSpace(cleaned[1]))
                break;

            cleaned = cleaned[1..].TrimStart();
        }
        return cleaned.ToString();
    }
}

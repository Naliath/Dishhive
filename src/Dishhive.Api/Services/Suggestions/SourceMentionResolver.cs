using System.Text.RegularExpressions;
using Dishhive.Api.Services.Import;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>
/// Resolves @[Source] and @domain references in planning instruction texts into website-host
/// constraints for the LLM's search tool ("find something vegetarian from
/// @[Dagelijkse Kost]"). A name is matched against the known sources
/// (<see cref="RecipeSourceCatalog"/>) case-insensitively, or a hand-typed domain is
/// taken as its own host; an unresolvable reference simply flows through as plain text.
/// Mirrors <see cref="CollectionMentionResolver"/> (which handles #[Collection]).
/// </summary>
public partial class SourceMentionResolver(RecipeSourceCatalog catalog)
{
    // Parallels the #[...] collection grammar; the @ trigger keeps the two namespaces distinct
    [GeneratedRegex(@"@\[([^\[\]\r\n]{1,100})\]")]
    private static partial Regex MentionRegex();

    // A hand-typed domain is unambiguous enough to accept without brackets. The
    // negative lookbehind prevents the domain part of an email address from becoming
    // a source mention.
    [GeneratedRegex(@"(?<![\w@])@((?:[a-zA-Z0-9-]+\.)+[a-zA-Z]{2,63})(?![\w.-])")]
    private static partial Regex BareDomainMentionRegex();

    /// <summary>The distinct source names referenced in a text</summary>
    public static IReadOnlyList<string> ExtractMentions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return MentionRegex().Matches(text)
            .Concat(BareDomainMentionRegex().Matches(text))
            .Select(match => match.Groups[1].Value.Trim())
            .Where(name => name.Length > 0)
            .DistinctBy(name => name.ToLowerInvariant())
            .ToList();
    }

    /// <summary>
    /// Resolves the @[Source] mentions across the given texts. A null date marks the
    /// global instructions; day texts carry their date so a specific day can be
    /// constrained to that site.
    /// </summary>
    public async Task<IReadOnlyList<SourceConstraint>> ResolveAsync(
        IReadOnlyList<(DateOnly? Date, string? Text)> sources,
        CancellationToken cancellationToken = default)
    {
        var datesByName = new Dictionary<string, SortedSet<DateOnly>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (date, text) in sources)
        {
            foreach (var name in ExtractMentions(text))
            {
                if (!datesByName.TryGetValue(name, out var dates))
                {
                    dates = [];
                    datesByName[name] = dates;
                }
                if (date != null)
                {
                    dates.Add(date.Value);
                }
            }
        }

        var constraints = new List<SourceConstraint>();
        foreach (var (name, dates) in datesByName)
        {
            var host = await catalog.ResolveHostAsync(name, cancellationToken);
            if (host == null)
            {
                continue; // unresolvable reference: stays a plain-text hint
            }

            constraints.Add(new SourceConstraint
            {
                Name = name,
                Host = host,
                Dates = dates.ToList()
            });
        }

        return constraints.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

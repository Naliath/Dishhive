using System.Text.RegularExpressions;
using Dishhive.Api.Data;
using Dishhive.Api.Services.Collections;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>
/// Resolves #[Collection Name] references in planning instruction texts into
/// concrete recipe-title constraints for the suggestion providers. References are
/// matched by name, case-insensitively, at use time — a dangling reference (renamed
/// or deleted collection) simply resolves to nothing and the raw text flows through
/// to the LLM as a plain hint. See docs/features/ai-week-planning.md.
/// </summary>
public partial class CollectionMentionResolver(
    DishhiveDbContext context,
    AutoCollectionProvider autoCollections)
{
    /// <summary>Max recipe titles included per referenced collection</summary>
    private const int MaxTitlesPerCollection = 15;

    // Collection names may not contain brackets (enforced at creation), which makes
    // this grammar collision-free
    [GeneratedRegex(@"#\[([^\[\]\r\n]{1,100})\]")]
    private static partial Regex MentionRegex();

    /// <summary>The distinct collection names referenced in a text</summary>
    public static IReadOnlyList<string> ExtractMentions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return MentionRegex().Matches(text)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(name => name.Length > 0)
            .DistinctBy(name => name.ToLowerInvariant())
            .ToList();
    }

    /// <summary>
    /// Resolves the mentions in the given texts. A null date marks the global
    /// instructions text; day texts carry their date so providers can constrain
    /// that specific day. Titles are ordered least-recently-planned first (so
    /// providers can prefer variety) and capped per collection.
    /// </summary>
    public async Task<IReadOnlyList<CollectionConstraint>> ResolveAsync(
        IReadOnlyList<(DateOnly? Date, string? Text)> sources,
        IReadOnlyDictionary<string, DateOnly>? lastPlannedByTitle = null,
        CancellationToken cancellationToken = default)
    {
        // Gather where each referenced name occurs (per-day and/or globally)
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

        var constraints = new List<CollectionConstraint>();
        foreach (var (name, dates) in datesByName)
        {
            var resolved = await ResolveTitlesAsync(name, cancellationToken);
            if (resolved == null)
            {
                continue; // dangling reference: stays a plain-text hint
            }

            var ordered = resolved.Value.Titles
                .OrderBy(t => lastPlannedByTitle != null && lastPlannedByTitle.TryGetValue(t, out var last)
                    ? last
                    : DateOnly.MinValue)
                .ThenBy(t => t, StringComparer.OrdinalIgnoreCase)
                .Take(MaxTitlesPerCollection)
                .ToList();

            constraints.Add(new CollectionConstraint
            {
                Name = resolved.Value.Name,
                RecipeTitles = ordered,
                Dates = dates.ToList()
            });
        }

        return constraints.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Member titles of the manual or auto collection with this name, or null</summary>
    private async Task<(string Name, List<string> Titles)?> ResolveTitlesAsync(
        string name, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();
        var manual = await context.Cookbooks
            .AsNoTracking()
            .Where(c => c.Name.ToLower() == lowered)
            .Select(c => new { c.Name, Titles = c.Entries.Select(e => e.Recipe!.Title).ToList() })
            .FirstOrDefaultAsync(cancellationToken);
        if (manual != null)
        {
            return (manual.Name, manual.Titles);
        }

        var auto = await autoCollections.FindByNameAsync(name, cancellationToken);
        if (auto != null)
        {
            var titles = await auto.ApplyFilter(context.Recipes.AsNoTracking())
                .Select(r => r.Title)
                .ToListAsync(cancellationToken);
            return (auto.Name, titles);
        }

        return null;
    }
}

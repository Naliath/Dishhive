using System.Text.RegularExpressions;
using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Collections;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>
/// Resolves #[Collection Name] references in planning instruction texts into
/// stable collection identities and full recipe-id membership for suggestion providers. References are
/// matched by name, case-insensitively, at use time. The request builder surfaces
/// unresolved names instead of silently widening the plan. Reviewed requirements
/// are subsequently resolved by stable identity. See docs/features/ai-week-planning.md.
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
    public Task<IReadOnlyList<CollectionConstraint>> ResolveAsync(
        IReadOnlyList<(DateOnly? Date, string? Text)> sources,
        IReadOnlyDictionary<string, DateOnly>? lastPlannedByTitle = null,
        CancellationToken cancellationToken = default)
        => ResolveAsync(sources.Select(source => (source.Date, MealType.Dinner, Course.Main, source.Text)).ToList(),
            lastPlannedByTitle, cancellationToken);

    public async Task<IReadOnlyList<CollectionConstraint>> ResolveAsync(
        IReadOnlyList<(DateOnly? Date, MealType MealType, Course Course, string? Text)> sources,
        IReadOnlyDictionary<string, DateOnly>? lastPlannedByTitle = null,
        CancellationToken cancellationToken = default)
    {
        var slotsByName = new Dictionary<string, HashSet<PlanningSlot>>(StringComparer.OrdinalIgnoreCase);
        var globalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (date, mealType, course, text) in sources)
        {
            foreach (var name in ExtractMentions(text))
            {
                if (!slotsByName.TryGetValue(name, out var slots))
                {
                    slots = [];
                    slotsByName[name] = slots;
                }
                if (date != null)
                {
                    slots.Add(new(date.Value, mealType, course));
                }
                else globalNames.Add(name);
            }
        }

        var constraints = new List<CollectionConstraint>();
        foreach (var (name, slots) in slotsByName)
        {
            var resolved = await ResolveCollectionAsync(name, byId: false, cancellationToken);
            if (resolved == null)
            {
                continue; // the request builder reports unresolved names
            }

            var ordered = resolved.Recipes
                .OrderBy(t => lastPlannedByTitle != null && lastPlannedByTitle.TryGetValue(t.Title, out var last)
                    ? last
                    : DateOnly.MinValue)
                .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .Take(MaxTitlesPerCollection)
                .Select(recipe => recipe.Title)
                .ToList();

            constraints.Add(new CollectionConstraint
            {
                Id = resolved.Id,
                Name = resolved.Name,
                RecipeIds = resolved.Recipes.Select(recipe => recipe.Id).ToList(),
                RecipeTitles = ordered,
                Dates = slots.Select(slot => slot.Date).Distinct().Order().ToList(),
                Slots = slots.OrderBy(slot => slot.Date).ThenBy(slot => slot.MealType).ThenBy(slot => slot.Course).ToList(),
                IsGlobal = globalNames.Contains(name)
            });
        }

        return constraints.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<CollectionConstraint?> ResolveByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveCollectionAsync(id, byId: true, cancellationToken);
        return resolved is null ? null : new CollectionConstraint
        {
            Id = resolved.Id, Name = resolved.Name,
            RecipeIds = resolved.Recipes.Select(recipe => recipe.Id).ToList(),
            RecipeTitles = resolved.Recipes.Take(MaxTitlesPerCollection).Select(recipe => recipe.Title).ToList()
        };
    }

    private sealed record MemberRecipe(Guid Id, string Title);
    private sealed record ResolvedCollection(string Id, string Name, List<MemberRecipe> Recipes);

    private async Task<ResolvedCollection?> ResolveCollectionAsync(
        string name, bool byId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();
        var hasGuid = Guid.TryParse(name, out var id);
        var manual = await context.Cookbooks
            .AsNoTracking()
            .Where(c => byId ? hasGuid && c.Id == id : c.Name.ToLower() == lowered)
            .Select(c => new { c.Id, c.Name, Recipes = c.Entries.Select(e => new MemberRecipe(e.RecipeId, e.Recipe!.Title)).ToList() })
            .FirstOrDefaultAsync(cancellationToken);
        if (manual != null)
        {
            return new(manual.Id.ToString(), manual.Name, manual.Recipes);
        }

        var auto = byId ? await autoCollections.FindByIdAsync(name, cancellationToken)
            : await autoCollections.FindByNameAsync(name, cancellationToken);
        if (auto != null)
        {
            var titles = await auto.ApplyFilter(context.Recipes.AsNoTracking())
                .Select(r => new MemberRecipe(r.Id, r.Title))
                .ToListAsync(cancellationToken);
            return new(auto.Id, auto.Name, titles);
        }

        return null;
    }
}

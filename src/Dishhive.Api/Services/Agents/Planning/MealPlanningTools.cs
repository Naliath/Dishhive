using System.ComponentModel;
using Dishhive.Api.Data;
using Dishhive.Api.Services.FreezyIntegration;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Agents.Planning;

/// <summary>
/// Provides the read-only "tools" the meal-planning agent calls into.
/// Methods here are wrapped as <c>AIFunction</c>s and exposed to the LLM. Each is small,
/// deterministic, and safe to call repeatedly.
///
/// Scoped per agent invocation so the underlying <c>DbContext</c> follows request lifetime.
/// </summary>
public sealed class MealPlanningTools
{
    private readonly DishhiveDbContext _db;
    private readonly IFreezyClient _freezy;

    public MealPlanningTools(DishhiveDbContext db, IFreezyClient freezy)
    {
        _db = db;
        _freezy = freezy;
    }

    [Description("List all family members with a one-line summary of their dietary preferences (allergies and dislikes are hard constraints).")]
    public async Task<IReadOnlyList<FamilyMemberSummary>> ListFamilyMembers()
    {
        var members = await _db.FamilyMembers.Include(m => m.Preferences).ToListAsync();
        return members.Select(m => new FamilyMemberSummary(
            m.Id,
            m.DisplayName,
            m.Preferences.Where(p => p.Kind.ToString() is "Allergy" or "Diet").Select(p => $"{p.Kind}: {p.Value}").ToList(),
            m.Preferences.Where(p => p.Kind.ToString() == "Dislike").Select(p => p.Value).ToList(),
            m.Preferences.Where(p => p.Kind.ToString() == "Like").Select(p => p.Value).ToList()
        )).ToList();
    }

    [Description("Search recipes by free-text and/or tag. Returns up to 50 candidates.")]
    public async Task<IReadOnlyList<RecipeCandidate>> SearchRecipes(
        [Description("Optional case-insensitive substring filter on the recipe title.")] string? query,
        [Description("Optional tag filter (e.g. 'vegetarian', 'pasta').")] string? tag)
    {
        IQueryable<Models.Recipes.Recipe> q = _db.Recipes.Include(r => r.Tags);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var needle = query.ToLowerInvariant();
            q = q.Where(r => r.Title.ToLower().Contains(needle));
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var t = tag.ToLowerInvariant();
            q = q.Where(r => r.Tags.Any(x => x.Tag.ToLower() == t));
        }
        var recipes = await q.OrderBy(r => r.Title).Take(50).ToListAsync();
        return recipes.Select(r => new RecipeCandidate(
            r.Id, r.Title, r.Description, r.Servings, r.Tags.Select(x => x.Tag).ToList())).ToList();
    }

    [Description("Get the dish history (what was planned in the last N days) so suggestions don't duplicate recent meals.")]
    public async Task<IReadOnlyList<HistoryRow>> GetRecentHistory(
        [Description("Number of days back to look. Defaults to 14.")] int? days)
    {
        var n = days ?? 14;
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-n));
        var rows = await _db.DishHistory
            .Where(h => h.Date >= from)
            .OrderByDescending(h => h.Date)
            .Select(h => new HistoryRow(h.Date, h.MealType.ToString(), h.DishLabel, h.RecipeId))
            .ToListAsync();
        return rows;
    }

    [Description("List currently frozen items in Freezy, useful for 'I have X in the freezer' style suggestions.")]
    public async Task<IReadOnlyList<FrozenItemReference>> GetFreezyItems()
    {
        try { return await _freezy.GetFrozenItemsAsync(); }
        catch { return Array.Empty<FrozenItemReference>(); }
    }

    public sealed record FamilyMemberSummary(
        Guid Id,
        string DisplayName,
        IReadOnlyList<string> HardConstraints,
        IReadOnlyList<string> Dislikes,
        IReadOnlyList<string> Likes);

    public sealed record RecipeCandidate(Guid Id, string Title, string? Description, int Servings, IReadOnlyList<string> Tags);

    public sealed record HistoryRow(DateOnly Date, string MealType, string DishLabel, Guid? RecipeId);
}

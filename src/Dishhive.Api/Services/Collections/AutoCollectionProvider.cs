using System.Text.Json;
using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Collections;

/// <summary>
/// A computed, read-only collection. <see cref="ApplyFilter"/> restricts a recipe
/// query to the collection's members, so auto collections compose with the regular
/// recipe list filters and never need stored membership rows.
/// </summary>
public record AutoCollection(
    string Id,
    string Name,
    Func<IQueryable<Recipe>, IQueryable<Recipe>> ApplyFilter);

/// <summary>
/// Provides the computed "auto" collections shown alongside manual collections:
/// Top rated, Quick, Recently added, and one favorites collection per active family
/// member. Computed on read — no rows, no background maintenance. Their names are
/// reserved (manual collections may not take them) so #[Name] mention resolution
/// stays unambiguous.
///
/// Each auto collection can be individually enabled/disabled from the settings page;
/// the disabled set is persisted as a UserSetting. Disabled collections drop out of
/// the cookbooks list, the recipe filter and mention resolution, but their names stay
/// reserved so re-enabling never collides with a manual collection.
/// </summary>
public class AutoCollectionProvider(DishhiveDbContext context)
{
    private const int TopRatedMinRatings = 2;
    private const double TopRatedMinAverage = 4.0;
    private const int QuickMaxMinutes = 30;
    private const int RecentDays = 30;

    public const string TopRatedId = "auto-top-rated";
    public const string QuickId = "auto-quick";
    public const string RecentId = "auto-recent";
    private const string MemberFavoritesIdPrefix = "auto-fav-";

    /// <summary>UserSetting key holding the JSON list of disabled auto-collection ids</summary>
    public const string DisabledSettingKey = "autoCollections.disabled";

    /// <summary>The enabled auto collections (the ones users actually see and reference)</summary>
    public async Task<IReadOnlyList<AutoCollection>> ListAsync(CancellationToken cancellationToken = default)
    {
        var all = await BuildAllAsync(cancellationToken);
        var disabled = await GetDisabledIdsAsync(cancellationToken);
        return all.Where(c => !disabled.Contains(c.Id)).ToList();
    }

    /// <summary>All auto collections with their enabled state, for the management UI</summary>
    public async Task<IReadOnlyList<(AutoCollection Collection, bool Enabled)>> ListWithStateAsync(
        CancellationToken cancellationToken = default)
    {
        var all = await BuildAllAsync(cancellationToken);
        var disabled = await GetDisabledIdsAsync(cancellationToken);
        return all.Select(c => (c, !disabled.Contains(c.Id))).ToList();
    }

    /// <summary>Enables or disables an auto collection; false when the id is unknown</summary>
    public async Task<bool> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default)
    {
        var all = await BuildAllAsync(cancellationToken);
        if (!all.Any(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var disabled = await GetDisabledIdsAsync(cancellationToken);
        if (enabled)
        {
            disabled.RemoveWhere(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            disabled.Add(id);
        }

        await SaveDisabledAsync(disabled, cancellationToken);
        return true;
    }

    /// <summary>Finds an enabled auto collection by its slug id, or null</summary>
    public async Task<AutoCollection?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var collections = await ListAsync(cancellationToken);
        return collections.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds an enabled auto collection by display name (case-insensitive), or null</summary>
    public async Task<AutoCollection?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var collections = await ListAsync(cancellationToken);
        return collections.FirstOrDefault(c => c.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether the name collides with an auto collection name — including disabled
    /// ones, so a manual collection can never claim a name a re-enable would reclaim.
    /// </summary>
    public async Task<bool> IsReservedNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var all = await BuildAllAsync(cancellationToken);
        return all.Any(c => c.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Builds every auto collection definition, regardless of enabled state</summary>
    private async Task<IReadOnlyList<AutoCollection>> BuildAllAsync(CancellationToken cancellationToken)
    {
        var collections = new List<AutoCollection>
        {
            new(TopRatedId, "Top rated", TopRatedFilter),
            new(QuickId, $"Quick (max {QuickMaxMinutes} min)", QuickFilter),
            new(RecentId, "Recently added", RecentFilter)
        };

        var members = await context.FamilyMembers
            .AsNoTracking()
            .Where(m => m.IsActive && !m.IsGuest)
            .OrderBy(m => m.Name)
            .Select(m => new { m.Id, m.Name })
            .ToListAsync(cancellationToken);

        foreach (var member in members)
        {
            var memberId = member.Id;
            collections.Add(new AutoCollection(
                $"{MemberFavoritesIdPrefix}{memberId}",
                $"{member.Name}'s favorites",
                query => query.Where(r => context.FamilyMemberFavorites
                    .Any(f => f.FamilyMemberId == memberId && f.RecipeId == r.Id))));
        }

        return collections;
    }

    private async Task<HashSet<string>> GetDisabledIdsAsync(CancellationToken cancellationToken)
    {
        var setting = await context.UserSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == DisabledSettingKey, cancellationToken);

        if (setting == null || string.IsNullOrWhiteSpace(setting.Value))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var ids = JsonSerializer.Deserialize<List<string>>(setting.Value);
            return ids != null
                ? new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task SaveDisabledAsync(HashSet<string> disabled, CancellationToken cancellationToken)
    {
        var value = JsonSerializer.Serialize(disabled.ToList());
        var setting = await context.UserSettings.FirstOrDefaultAsync(s => s.Key == DisabledSettingKey, cancellationToken);
        if (setting == null)
        {
            context.UserSettings.Add(new UserSetting { Key = DisabledSettingKey, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<Recipe> TopRatedFilter(IQueryable<Recipe> query)
    {
        return query.Where(r =>
            context.MealRatings.Count(mr => mr.PlannedMeal!.RecipeId == r.Id) >= TopRatedMinRatings
            && context.MealRatings
                .Where(mr => mr.PlannedMeal!.RecipeId == r.Id)
                .Average(mr => (double)mr.Rating) >= TopRatedMinAverage);
    }

    private static IQueryable<Recipe> QuickFilter(IQueryable<Recipe> query)
    {
        return query.Where(r => r.TotalTimeMinutes != null && r.TotalTimeMinutes <= QuickMaxMinutes);
    }

    private static IQueryable<Recipe> RecentFilter(IQueryable<Recipe> query)
    {
        var cutoff = DateTime.UtcNow.AddDays(-RecentDays);
        return query.Where(r => r.CreatedAt >= cutoff);
    }
}

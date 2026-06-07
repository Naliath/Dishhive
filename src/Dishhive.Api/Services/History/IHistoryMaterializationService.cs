using Dishhive.Api.Data;
using Dishhive.Api.Models.History;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.History;

/// <summary>
/// Materializes <see cref="DishHistoryEntry"/> rows from past <c>MealSlot</c>s.
///
/// Cheap implementation: scans week plans whose <c>WeekStart</c> is before today's Monday,
/// and ensures each populated past slot has a corresponding history entry. Idempotent —
/// uses (PlannedSlotId) as the natural identity to avoid duplicates.
/// </summary>
public interface IHistoryMaterializationService
{
    Task<int> MaterializeAsync(CancellationToken ct = default);
}

public sealed class HistoryMaterializationService : IHistoryMaterializationService
{
    private readonly DishhiveDbContext _db;

    public HistoryMaterializationService(DishhiveDbContext db) => _db = db;

    public async Task<int> MaterializeAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var pastSlots = await _db.MealSlots
            .Include(s => s.WeekPlan)
            .Where(s => s.WeekPlan!.WeekStart < today)
            .Where(s => s.RecipeId != null
                     || (s.VagueIntent != null && s.VagueIntent != "")
                     || (s.FrozenItemRef != null && s.FrozenItemRef != ""))
            .ToListAsync(ct);

        var existingSlotIds = await _db.DishHistory
            .Where(h => h.PlannedSlotId != null)
            .Select(h => h.PlannedSlotId!.Value)
            .ToListAsync(ct);
        var existing = new HashSet<Guid>(existingSlotIds);

        var recipeTitles = await LoadRecipeTitlesAsync(pastSlots.Select(s => s.RecipeId).Where(id => id.HasValue).Select(id => id!.Value), ct);

        var added = 0;
        foreach (var slot in pastSlots)
        {
            if (existing.Contains(slot.Id)) continue;

            var label = ResolveLabel(slot, recipeTitles);
            if (string.IsNullOrWhiteSpace(label)) continue;

            var slotDate = SlotDate(slot.WeekPlan!.WeekStart, slot.DayOfWeek);
            if (slotDate >= today) continue;

            _db.DishHistory.Add(new DishHistoryEntry
            {
                Date = slotDate,
                MealType = slot.MealType,
                RecipeId = slot.RecipeId,
                DishLabel = label,
                PlannedSlotId = slot.Id,
            });
            added++;
        }

        if (added > 0) await _db.SaveChangesAsync(ct);
        return added;
    }

    private async Task<Dictionary<Guid, string>> LoadRecipeTitlesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0) return new();
        return await _db.Recipes
            .Where(r => idList.Contains(r.Id))
            .Select(r => new { r.Id, r.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, ct);
    }

    private static string ResolveLabel(Models.Planning.MealSlot slot, IReadOnlyDictionary<Guid, string> recipeTitles)
    {
        if (slot.RecipeId.HasValue && recipeTitles.TryGetValue(slot.RecipeId.Value, out var title))
            return title;
        if (!string.IsNullOrWhiteSpace(slot.VagueIntent))
            return slot.VagueIntent!;
        if (!string.IsNullOrWhiteSpace(slot.FrozenItemRef))
            return $"Freezy: {slot.FrozenItemRef}";
        return string.Empty;
    }

    private static DateOnly SlotDate(DateOnly weekStart, DayOfWeek dow)
    {
        var offset = dow == DayOfWeek.Sunday ? 6 : (int)dow - 1;
        return weekStart.AddDays(offset);
    }
}

using Dishhive.Api.Data;
using Dishhive.Api.Models.Shopping;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Shopping;

public interface IShoppingListGenerationService
{
    Task<ShoppingList> GenerateFromWeekPlanAsync(Guid weekPlanId, CancellationToken ct = default);
}

/// <summary>
/// Builds a shopping list by aggregating ingredients across every recipe-backed slot
/// in the week plan. Freezy slots are skipped (already in the freezer). Vague-intent
/// slots are added as plain reminder lines so the cook can flesh them out.
/// </summary>
public sealed class ShoppingListGenerationService : IShoppingListGenerationService
{
    private readonly DishhiveDbContext _db;

    public ShoppingListGenerationService(DishhiveDbContext db) => _db = db;

    public async Task<ShoppingList> GenerateFromWeekPlanAsync(Guid weekPlanId, CancellationToken ct = default)
    {
        var plan = await _db.WeekPlans
            .Include(p => p.Slots)
            .FirstOrDefaultAsync(p => p.Id == weekPlanId, ct)
            ?? throw new KeyNotFoundException($"Week plan {weekPlanId} not found.");

        var recipeIds = plan.Slots
            .Where(s => s.RecipeId.HasValue && string.IsNullOrEmpty(s.FrozenItemRef))
            .Select(s => s.RecipeId!.Value)
            .Distinct()
            .ToList();

        var recipes = await _db.Recipes
            .Include(r => r.Ingredients)
            .Where(r => recipeIds.Contains(r.Id))
            .ToListAsync(ct);

        // Aggregate ingredients keyed by (lowercase name, lowercase unit).
        // Different units don't merge — they become separate lines.
        var aggregated = new Dictionary<(string name, string unit), AggregatedIngredient>();
        foreach (var recipe in recipes)
        {
            foreach (var ing in recipe.Ingredients)
            {
                var key = (ing.Name.Trim().ToLowerInvariant(), (ing.Unit ?? "").Trim().ToLowerInvariant());
                if (!aggregated.TryGetValue(key, out var existing))
                {
                    aggregated[key] = new AggregatedIngredient
                    {
                        DisplayName = ing.Name.Trim(),
                        Unit = ing.Unit,
                        Quantity = ing.Quantity,
                        Section = ing.Section,
                    };
                }
                else
                {
                    if (ing.Quantity.HasValue)
                        existing.Quantity = (existing.Quantity ?? 0) + ing.Quantity.Value;
                }
            }
        }

        var list = new ShoppingList
        {
            WeekPlanId = plan.Id,
            Title = $"Week of {plan.WeekStart:yyyy-MM-dd}",
        };

        var order = 0;
        foreach (var item in aggregated.Values
                    .OrderBy(a => a.Section ?? "~")
                    .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            list.Items.Add(new ShoppingListItem
            {
                Order = order++,
                Name = item.DisplayName,
                Quantity = item.Quantity,
                Unit = item.Unit,
                Section = item.Section,
            });
        }

        // Vague-intent reminders (skip Freezy slots — already in inventory).
        foreach (var slot in plan.Slots
                    .Where(s => !s.RecipeId.HasValue
                             && string.IsNullOrEmpty(s.FrozenItemRef)
                             && !string.IsNullOrWhiteSpace(s.VagueIntent)))
        {
            list.Items.Add(new ShoppingListItem
            {
                Order = order++,
                Name = $"(Plan: {slot.VagueIntent})",
                Section = "Reminders",
                Note = $"{slot.DayOfWeek} {slot.MealType}",
            });
        }

        _db.ShoppingLists.Add(list);
        await _db.SaveChangesAsync(ct);
        return list;
    }

    private sealed class AggregatedIngredient
    {
        public string DisplayName { get; set; } = string.Empty;
        public string? Unit { get; set; }
        public decimal? Quantity { get; set; }
        public string? Section { get; set; }
    }
}

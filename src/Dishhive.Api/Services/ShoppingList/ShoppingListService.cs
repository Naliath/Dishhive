using Dishhive.Api.Data;
using Dishhive.Api.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.ShoppingList;

public interface IShoppingListService
{
    /// <summary>Generates a shopping list from the planned meals in a date range (inclusive)</summary>
    Task<ShoppingListDto> GenerateAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

/// <summary>
/// Computes a shopping list on demand from planned meals and recipe ingredients —
/// nothing is persisted (see docs/features/shopping-list-export.md).
///
/// Rules:
/// - Meals sourced from the freezer (FreezyItemRef set) are skipped entirely.
/// - Recipe quantities scale by attendees / recipe servings (factor 1 when no attendees).
/// - Ingredients aggregate by case-insensitive name + canonical unit.
/// - Unparseable lines (no quantity) pass through verbatim, aggregated by name only.
/// - Meals without a recipe become reminders instead of ingredients.
/// </summary>
public class ShoppingListService : IShoppingListService
{
    private readonly DishhiveDbContext _context;

    public ShoppingListService(DishhiveDbContext context)
    {
        _context = context;
    }

    public async Task<ShoppingListDto> GenerateAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var meals = await _context.PlannedMeals
            .AsNoTracking()
            .Include(m => m.Recipe!)
                .ThenInclude(r => r.Ingredients)
            .Include(m => m.Attendees)
            .Where(m => m.Date >= from && m.Date <= to)
            .OrderBy(m => m.Date)
            .ToListAsync(cancellationToken);

        var aggregated = new Dictionary<(string Name, string? Unit), ShoppingListItemDto>();
        var reminders = new List<ShoppingListReminderDto>();

        foreach (var meal in meals)
        {
            // Freezer meals are already cooked and stored; nothing to buy
            if (!string.IsNullOrEmpty(meal.FreezyItemRef))
            {
                continue;
            }

            if (meal.Recipe == null)
            {
                reminders.Add(new ShoppingListReminderDto
                {
                    Date = meal.Date,
                    Text = meal.DishName ?? meal.VagueInstruction ?? "(unspecified)"
                });
                continue;
            }

            var recipe = meal.Recipe;
            var factor = meal.Attendees.Count > 0 && recipe.Servings > 0
                ? (decimal)meal.Attendees.Count / recipe.Servings
                : 1m;

            foreach (var ingredient in recipe.Ingredients)
            {
                var key = (ingredient.Name.ToLowerInvariant(), ingredient.Quantity.HasValue ? ingredient.Unit : null);

                if (!aggregated.TryGetValue(key, out var item))
                {
                    item = new ShoppingListItemDto
                    {
                        Name = ingredient.Name,
                        Unit = ingredient.Quantity.HasValue ? ingredient.Unit : null
                    };
                    aggregated[key] = item;
                }

                if (ingredient.Quantity.HasValue)
                {
                    item.Quantity = (item.Quantity ?? 0) + Math.Round(ingredient.Quantity.Value * factor, 2);
                }

                if (!item.SourceRecipes.Contains(recipe.Title))
                {
                    item.SourceRecipes.Add(recipe.Title);
                }
            }
        }

        return new ShoppingListDto
        {
            From = from,
            To = to,
            Items = aggregated.Values.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            Reminders = reminders
        };
    }
}

using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Freezy;

/// <summary>
/// Computes the freezer stock actually available for new planning: Freezy's current
/// quantities minus what already-planned meals have reserved. A future, not-yet-eaten
/// meal reserves its freezer units because Freezy hasn't been decremented for it yet;
/// past meals are trusted to Freezy, whose stock is updated on consumption (a past meal
/// that was never consumed simply leaves its stock standing in Freezy). This stops the
/// same frozen stock from being planned into two different meals or weeks.
/// </summary>
public class FreezerAvailabilityService
{
    private readonly IFreezyClient _freezyClient;
    private readonly DishhiveDbContext _context;

    public FreezerAvailabilityService(IFreezyClient freezyClient, DishhiveDbContext context)
    {
        _freezyClient = freezyClient;
        _context = context;
    }

    /// <summary>Whether the Freezy integration is configured (delegates to the client)</summary>
    public bool IsConfigured => _freezyClient.IsConfigured;

    /// <summary>
    /// Frozen items with quantities reduced by future un-consumed reservations, soonest
    /// expiring first. Items with nothing left to plan are dropped. Returns empty when
    /// Freezy is unconfigured or unreachable.
    /// </summary>
    public async Task<IReadOnlyList<FrozenItem>> GetAvailableAsync(CancellationToken cancellationToken = default)
    {
        var stock = await _freezyClient.GetFrozenItemsAsync(cancellationToken);
        if (stock.Count == 0)
        {
            return [];
        }

        var today = DateOnly.FromDateTime(DateTime.Today);

        // Future, not-yet-eaten freezer meals reserve stock Freezy hasn't decremented.
        // An already-eaten meal (even today's) is reflected in Freezy, so don't double-count.
        var reservedByItem = await _context.PlannedMeals
            .AsNoTracking()
            .Where(m => m.FreezyItemRef != null
                && m.FreezyItemQuantity > 0
                && m.Date >= today
                && m.Eaten != EatenStatus.Eaten)
            .GroupBy(m => m.FreezyItemRef!)
            .Select(g => new { ItemId = g.Key, Reserved = g.Sum(m => m.FreezyItemQuantity) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Reserved, cancellationToken);

        if (reservedByItem.Count == 0)
        {
            return stock;
        }

        var available = new List<FrozenItem>(stock.Count);
        foreach (var item in stock)
        {
            var remaining = item.Quantity - reservedByItem.GetValueOrDefault(item.Id);
            if (remaining > 0)
            {
                available.Add(remaining == item.Quantity ? item : item with { Quantity = remaining });
            }
        }

        return available; // stock is already ordered soonest-expiring first
    }
}

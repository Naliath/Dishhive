using Dishhive.Api.Services.Time;
using System.Security.Cryptography;
using System.Text.Json;
using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Api;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Freezy;

/// <summary>Reconciles local reservations against an observed external snapshot; never writes to Freezy.</summary>
public sealed class FreezerReservationService(IFreezyClient freezy, DishhiveDbContext context, IHouseholdClock clock)
{
    public static string Fingerprint(FrozenItem item) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new { item.Id, item.Name, item.Unit, item.ExpirationDate, item.Notes })));

    public async Task CaptureAsync(PlannedMeal meal, string? previousItemRef, CancellationToken token)
    {
        if (meal.FreezyItemRef == previousItemRef) return;
        meal.FreezyItemFingerprint = null;
        if (meal.FreezyItemRef == null || !freezy.IsConfigured) return;
        var snapshot = await freezy.GetStockSnapshotAsync(token);
        var item = snapshot?.Available == true ? snapshot.Items.FirstOrDefault(item => item.Id == meal.FreezyItemRef) : null;
        if (item != null) meal.FreezyItemFingerprint = Fingerprint(item);
    }

    private IQueryable<PlannedMeal> Reservations() => context.PlannedMeals.Where(meal =>
        meal.FreezyItemRef != null && meal.FreezyItemQuantity > 0 && meal.Eaten == null
        && meal.Date >= clock.Today);

    public async Task<FreezerReconciliationDto> CheckAsync(CancellationToken token)
    {
        if (!freezy.IsConfigured) return new("unconfigured", DateTime.UtcNow, []);
        var snapshot = await freezy.GetStockSnapshotAsync(token);
        if (snapshot?.Available != true) return new("unavailable", snapshot?.CheckedAt ?? DateTime.UtcNow, []);
        var meals = await Reservations().AsNoTracking().OrderBy(meal => meal.Date).ThenBy(meal => meal.Id).ToListAsync(token);
        var stock = snapshot.Items.ToDictionary(item => item.Id);
        var issues = new List<FreezerReservationIssueDto>();
        foreach (var group in meals.GroupBy(meal => meal.FreezyItemRef!))
        {
            stock.TryGetValue(group.Key, out var item);
            var reserved = group.Sum(meal => meal.FreezyItemQuantity);
            var fingerprint = item == null ? null : Fingerprint(item);
            var issue = item == null ? "missing" : reserved > item.Quantity ? "shortage"
                : group.Any(meal => meal.FreezyItemFingerprint == null) ? "unverified"
                : group.Any(meal => meal.FreezyItemFingerprint != fingerprint) ? "changed" : null;
            if (issue == null) continue;
            issues.Add(new(group.Key, item?.Name, fingerprint, item?.Quantity ?? 0, reserved,
                Math.Max(0, reserved - (item?.Quantity ?? 0)), issue,
                group.Select(meal => new FreezerReservationMealDto(meal.Id, meal.Date,
                    meal.DishName ?? meal.VagueInstruction ?? "", meal.FreezyItemQuantity)).ToList()));
        }
        return new("ready", snapshot.CheckedAt, issues);
    }

    public async Task ResolveAsync(Guid id, ResolveFreezerReservationDto request, CancellationToken token)
    {
        var meal = await Reservations().FirstOrDefaultAsync(meal => meal.Id == id, token);
        if (meal == null || meal.FreezyItemRef != request.ItemRef || meal.FreezyItemQuantity != request.Quantity)
            throw Conflict();
        if (request.Action == "release")
        {
            meal.FreezyItemRef = null;
            meal.FreezyItemQuantity = 0;
            meal.FreezyItemFingerprint = null;
        }
        else
        {
            var snapshot = await freezy.GetStockSnapshotAsync(token);
            if (snapshot?.Available != true)
                throw new ApiProblemException(503, "freezy_unavailable", "errors.unavailableTitle", "errors.unavailableDetail");
            var item = snapshot.Items.FirstOrDefault(item => item.Id == request.ItemRef);
            var reserved = await Reservations().Where(meal => meal.FreezyItemRef == request.ItemRef)
                .SumAsync(meal => meal.FreezyItemQuantity, token);
            if (item == null || item.Quantity < reserved || Fingerprint(item) != request.Fingerprint) throw Conflict();
            meal.FreezyItemFingerprint = request.Fingerprint;
        }
        await context.SaveChangesAsync(token);
    }

    private static ApiProblemException Conflict() => new(409, "reservation_changed", "errors.conflictTitle", "errors.conflictDetail");
}

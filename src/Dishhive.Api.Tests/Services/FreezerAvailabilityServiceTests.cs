using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Freezy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// Tests the freezer stock accounting: future un-eaten meals reserve stock; past meals
/// are trusted to Freezy; reservations sum by item id and depleted items drop out.
/// </summary>
public class FreezerAvailabilityServiceTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private sealed class StubFreezyClient(IReadOnlyList<FrozenItem> items) : IFreezyClient
    {
        public bool IsConfigured => true;
        public string? BaseUrl => "stub";
        public string Transport => "stub";
        public Task<bool> IsReachableAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<FrozenItem>> GetFrozenItemsAsync(CancellationToken ct = default)
            => Task.FromResult(items);
    }

    private static DishhiveDbContext NewContext() =>
        new(new DbContextOptionsBuilder<DishhiveDbContext>()
            .UseInMemoryDatabase($"freezer-avail-{Guid.NewGuid()}")
            .Options);

    private static PlannedMeal FreezerMeal(string itemId, int qty, DateOnly date, EatenStatus? eaten = null) => new()
    {
        Id = Guid.NewGuid(),
        Date = date,
        MealType = MealType.Dinner,
        Course = Course.Main,
        DishName = "Frozen thing",
        FreezyItemRef = itemId,
        FreezyItemQuantity = qty,
        Eaten = eaten
    };

    private static async Task<IReadOnlyList<FrozenItem>> AvailableAsync(
        IReadOnlyList<FrozenItem> stock, params PlannedMeal[] meals)
    {
        await using var context = NewContext();
        if (meals.Length > 0)
        {
            context.PlannedMeals.AddRange(meals);
            await context.SaveChangesAsync();
        }
        var service = new FreezerAvailabilityService(new StubFreezyClient(stock), context);
        return await service.GetAvailableAsync();
    }

    [Fact]
    public async Task FutureMeal_ReservesStock()
    {
        var stock = new[] { new FrozenItem { Id = "a", Name = "Lasagna", Quantity = 3 } };

        var available = await AvailableAsync(stock, FreezerMeal("a", 1, Today.AddDays(2)));

        available.Should().ContainSingle().Which.Quantity.Should().Be(2);
    }

    [Fact]
    public async Task PastMeal_DoesNotReserve_TrustsFreezyStock()
    {
        var stock = new[] { new FrozenItem { Id = "a", Name = "Lasagna", Quantity = 3 } };

        var available = await AvailableAsync(stock, FreezerMeal("a", 1, Today.AddDays(-2)));

        available.Should().ContainSingle().Which.Quantity.Should().Be(3);
    }

    [Fact]
    public async Task FutureEatenMeal_DoesNotReserve_AlreadyReflectedInFreezy()
    {
        var stock = new[] { new FrozenItem { Id = "a", Name = "Lasagna", Quantity = 3 } };

        var available = await AvailableAsync(stock, FreezerMeal("a", 1, Today, EatenStatus.Eaten));

        available.Should().ContainSingle().Which.Quantity.Should().Be(3);
    }

    [Fact]
    public async Task FullyReservedItem_IsDropped()
    {
        var stock = new[] { new FrozenItem { Id = "a", Name = "Lasagna", Quantity = 1 } };

        var available = await AvailableAsync(stock, FreezerMeal("a", 1, Today.AddDays(1)));

        available.Should().BeEmpty();
    }

    [Fact]
    public async Task ReservationsForSameItem_AreSummed()
    {
        var stock = new[] { new FrozenItem { Id = "a", Name = "Lasagna", Quantity = 5 } };

        var available = await AvailableAsync(stock,
            FreezerMeal("a", 2, Today.AddDays(1)),
            FreezerMeal("a", 1, Today.AddDays(3)));

        available.Should().ContainSingle().Which.Quantity.Should().Be(2);
    }

    [Fact]
    public async Task NoStock_ReturnsEmpty()
    {
        (await AvailableAsync([])).Should().BeEmpty();
    }
}

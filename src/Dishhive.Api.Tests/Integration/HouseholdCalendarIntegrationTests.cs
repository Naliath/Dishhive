using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Time;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Dishhive.Api.Tests.Integration;

public sealed class HouseholdCalendarIntegrationTests
{
    [Fact]
    public async Task FeedbackHistoryAndFreezerShareInjectedHouseholdDay()
    {
        var day = new DateOnly(2040, 3, 26); // Deliberately unrelated to the machine date.
        var freezy = Substitute.For<IFreezyClient>();
        freezy.GetFrozenItemsAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<FrozenItem>>([new() { Id = "stock", Name = "Soup", Quantity = 10 }]));
        using var factory = new TestWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHouseholdClock>();
            services.AddSingleton<IHouseholdClock>(new TestHouseholdClock(day));
            services.RemoveAll<IFreezyClient>();
            services.AddSingleton(freezy);
        }));
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var yesterday = new PlannedMeal { Date = day.AddDays(-1), DishName = "Yesterday", FreezyItemRef = "stock", FreezyItemQuantity = 3 };
        var today = new PlannedMeal { Date = day, DishName = "Today", FreezyItemRef = "stock", FreezyItemQuantity = 2 };
        var tomorrow = new PlannedMeal { Date = day.AddDays(1), DishName = "Tomorrow", FreezyItemRef = "stock", FreezyItemQuantity = 1 };
        db.PlannedMeals.AddRange(yesterday, today, tomorrow);
        await db.SaveChangesAsync();

        var stock = await scope.ServiceProvider.GetRequiredService<FreezerAvailabilityService>().GetAvailableAsync();
        Assert.Equal(7, Assert.Single(stock).Quantity);
        var statistics = await client.GetFromJsonAsync<DishStatisticsDto>("/api/statistics/dishes");
        Assert.Equal(2, statistics!.Dishes.Count);
        Assert.DoesNotContain(statistics.Dishes, dish => dish.DishName == "Tomorrow");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/plannedmeals/{tomorrow.Id}/eaten",
            new SetEatenDto { Status = EatenStatus.Eaten })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/plannedmeals/{today.Id}/eaten",
            new SetEatenDto { Status = EatenStatus.Eaten })).StatusCode);
        var preferences = await client.GetFromJsonAsync<UserPreferencesDto>("/api/settings/preferences");
        Assert.Equal("Europe/Brussels", preferences!.HouseholdTimeZone);
    }
}


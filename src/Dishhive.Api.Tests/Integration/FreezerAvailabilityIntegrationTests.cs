using System.Net.Http.Json;
using Dishhive.Api.Controllers;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Freezy;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Dishhive.Api.Tests.Integration;

/// <summary>
/// End-to-end: a future freezer meal reserves stock, so /api/freezer/suggestions
/// reports the reduced quantity (the same stock can't be planned twice).
/// </summary>
public class FreezerAvailabilityIntegrationTests
{
    private sealed class StubFreezyClient : IFreezyClient
    {
        public bool IsConfigured => true;
        public string? BaseUrl => "stub";
        public string Transport => "stub";
        public Task<bool> IsReachableAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<FrozenItem>> GetFrozenItemsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FrozenItem>>(
                [new FrozenItem { Id = "lasagna-1", Name = "Frozen lasagna", Quantity = 3 }]);
    }

    private sealed class StubFreezyFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IFreezyClient>(new StubFreezyClient()));
        }
    }

    [Fact]
    public async Task FutureFreezerMeal_ReducesAvailableStock()
    {
        using var factory = new StubFreezyFactory();
        using var client = factory.CreateClient();

        var before = await client.GetFromJsonAsync<FreezerSuggestionsDto>("/api/freezer/suggestions");
        before!.Enabled.Should().BeTrue();
        before.Items.Should().ContainSingle().Which.Quantity.Should().Be(3);

        // Plan a future freezer meal consuming 1 unit
        var create = await client.PostAsJsonAsync("/api/plannedmeals", new CreatePlannedMealDto
        {
            Date = DateOnly.FromDateTime(DateTime.Today).AddDays(2),
            DishName = "Frozen lasagna",
            FreezyItemRef = "lasagna-1",
            FreezyItemQuantity = 1
        });
        create.IsSuccessStatusCode.Should().BeTrue();

        var after = await client.GetFromJsonAsync<FreezerSuggestionsDto>("/api/freezer/suggestions");
        after!.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
    }
}

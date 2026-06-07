using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

public class HistoryControllerTests : TestBase
{
    [Fact]
    public async Task Empty_history_returns_empty()
    {
        var entries = await Client.GetFromJsonAsync<List<DishHistoryEntryDto>>("/api/history");
        entries.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task DishFrequency_endpoint_returns_empty_collection_when_no_data()
    {
        var resp = await Client.GetFromJsonAsync<List<DishFrequencyDto>>("/api/statistics/dish-frequency");
        resp.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task Favorites_for_unknown_member_return_404()
    {
        var resp = await Client.GetAsync($"/api/family-members/{Guid.NewGuid()}/favorites");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Favorites_can_be_added_and_listed()
    {
        var member = await (await Client.PostAsJsonAsync("/api/family-members",
            new CreateFamilyMemberDto { DisplayName = "Sam" }))
            .Content.ReadFromJsonAsync<FamilyMemberDto>();

        var add = await Client.PostAsJsonAsync($"/api/family-members/{member!.Id}/favorites",
            new CreateFavoriteDto { DishLabel = "Pizza" });
        add.StatusCode.Should().Be(HttpStatusCode.Created);

        var favorites = await Client.GetFromJsonAsync<List<DishFavoriteDto>>(
            $"/api/family-members/{member.Id}/favorites");
        favorites.Should().HaveCount(1);
        favorites![0].DishLabel.Should().Be("Pizza");
    }
}

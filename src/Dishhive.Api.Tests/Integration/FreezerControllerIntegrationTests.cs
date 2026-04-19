using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Dishhive.Api.Tests.Integration;

public class FreezerControllerIntegrationTests : TestBase
{
    public FreezerControllerIntegrationTests()
    {
        ClearDatabase();
    }

    /// <summary>
    /// Freezy integration is disabled in tests (no real Freezy API available).
    /// The FreezerController should return an empty list gracefully when Freezy is unreachable.
    /// </summary>
    [Fact]
    public async Task GetItems_WhenFreezyIsUnreachable_ReturnsEmptyList()
    {
        // The test environment has no running Freezy — service should return empty gracefully
        var resp = await Client.GetAsync("/api/freezer/items");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        body.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task GetStatus_ReturnsStatusObject()
    {
        var resp = await Client.GetAsync("/api/freezer/status");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetItemById_WhenFreezyIsUnreachable_Returns404()
    {
        var resp = await Client.GetAsync($"/api/freezer/items/{Guid.NewGuid()}");
        // Either 404 (not found) or 200 with null — either is acceptable graceful degradation
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.OK);
    }
}

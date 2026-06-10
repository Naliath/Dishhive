using System.Net;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Tests.Mocks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// Tests for the Freezy integration boundary: mapping, expiration ordering,
/// and graceful degradation when Freezy is unconfigured or unreachable
/// </summary>
public class FreezyHttpClientTests
{
    private const string FreezyBaseUrl = "http://freezy.test/";

    private static FreezyHttpClient CreateClient(HttpMessageHandler handler, bool configured = true)
    {
        var httpClient = new HttpClient(handler);
        if (configured)
        {
            httpClient.BaseAddress = new Uri(FreezyBaseUrl);
        }
        return new FreezyHttpClient(httpClient, NullLogger<FreezyHttpClient>.Instance);
    }

    [Fact]
    public void IsConfigured_WithoutBaseUrl_ReturnsFalse()
    {
        var client = CreateClient(new MockHttpMessageHandler(), configured: false);

        client.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task GetFrozenItems_Unconfigured_ReturnsEmptyWithoutRequest()
    {
        var handler = new MockHttpMessageHandler();
        var client = CreateClient(handler, configured: false);

        var items = await client.GetFrozenItemsAsync();

        items.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFrozenItems_MapsFreezyDtoToFrozenItems_OrderedByExpiration()
    {
        var json = """
            [
              { "id": "b", "name": "Soep", "quantity": 2, "unit": "portions",
                "expirationDate": "2026-09-01T00:00:00Z", "notes": "tomatensoep" },
              { "id": "a", "name": "Lasagne", "quantity": 1, "unit": "pieces",
                "expirationDate": "2026-07-01T00:00:00Z" },
              { "id": "c", "name": "Zonder datum", "quantity": 1 }
            ]
            """;
        var handler = new MockHttpMessageHandler()
            .RespondWith(FreezyBaseUrl + "api/items", json, "application/json");
        var client = CreateClient(handler);

        var items = await client.GetFrozenItemsAsync();

        items.Should().HaveCount(3);
        // Soonest-expiring first; items without a date go last
        items.Select(i => i.Name).Should().Equal("Lasagne", "Soep", "Zonder datum");
        items[1].Quantity.Should().Be(2);
        items[1].Unit.Should().Be("portions");
        items[1].Notes.Should().Be("tomatensoep");
    }

    [Fact]
    public async Task GetFrozenItems_SkipsItemsWithoutName()
    {
        var json = """[ { "id": "x" }, { "id": "y", "name": "Geldig" } ]""";
        var handler = new MockHttpMessageHandler()
            .RespondWith(FreezyBaseUrl + "api/items", json, "application/json");
        var client = CreateClient(handler);

        var items = await client.GetFrozenItemsAsync();

        items.Should().ContainSingle(i => i.Name == "Geldig");
    }

    [Fact]
    public async Task GetFrozenItems_FreezyReturnsServerError_ReturnsEmpty()
    {
        var handler = new MockHttpMessageHandler()
            .FailWith(FreezyBaseUrl + "api/items", HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        var items = await client.GetFrozenItemsAsync();

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFrozenItems_FreezyReturnsInvalidJson_ReturnsEmpty()
    {
        var handler = new MockHttpMessageHandler()
            .RespondWith(FreezyBaseUrl + "api/items", "<html>boom</html>", "application/json");
        var client = CreateClient(handler);

        var items = await client.GetFrozenItemsAsync();

        items.Should().BeEmpty();
    }
}

using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

public class HealthEndpointTests : TestBase
{
    [Fact]
    public async Task Get_health_returns_ok()
    {
        var response = await Client.GetAsync("/health");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_version_returns_payload()
    {
        var response = await Client.GetAsync("/api/version");

        response.IsSuccessStatusCode.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(AppVersion.Version);
    }
}

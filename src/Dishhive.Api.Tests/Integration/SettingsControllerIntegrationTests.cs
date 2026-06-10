using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

public class SettingsControllerIntegrationTests : TestBase
{
    [Fact]
    public async Task GetSetting_UnknownKey_ReturnsNotFound()
    {
        // measurementSystem is metric by absence: no row until the user changes it
        var response = await Client.GetAsync("/api/settings/measurementSystem");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetSetting_NewKey_CreatesSetting()
    {
        var response = await Client.PutAsJsonAsync(
            "/api/settings/measurementSystem",
            new UpsertUserSettingDto { Value = "imperial" });

        var created = await response.Content.ReadFromJsonAsync<UserSettingDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.Key.Should().Be("measurementSystem");
        created.Value.Should().Be("imperial");
    }

    [Fact]
    public async Task SetSetting_ExistingKey_UpdatesValue()
    {
        await Client.PutAsJsonAsync("/api/settings/measurementSystem", new UpsertUserSettingDto { Value = "imperial" });

        var response = await Client.PutAsJsonAsync("/api/settings/measurementSystem", new UpsertUserSettingDto { Value = "metric" });
        var updated = await response.Content.ReadFromJsonAsync<UserSettingDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.Value.Should().Be("metric");
    }

    [Fact]
    public async Task DeleteSetting_ExistingKey_RemovesSetting()
    {
        await Client.PutAsJsonAsync("/api/settings/measurementSystem", new UpsertUserSettingDto { Value = "imperial" });

        var deleteResponse = await Client.DeleteAsync("/api/settings/measurementSystem");
        var getResponse = await Client.GetAsync("/api/settings/measurementSystem");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

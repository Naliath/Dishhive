using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Controllers;
using Dishhive.Api.Models;
using FluentAssertions;
using Xunit;

namespace Dishhive.Api.Tests.Integration;

public class SettingsControllerIntegrationTests : TestBase
{
    public SettingsControllerIntegrationTests()
    {
        ClearDatabase();
    }

    [Fact]
    public async Task GetAll_WhenNoSettings_ReturnsEmptyDictionary()
    {
        var response = await Client.GetAsync("/api/settings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dict = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        dict.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task Upsert_CreatesNewSetting()
    {
        var response = await Client.PutAsJsonAsync("/api/settings/week_start_day", new { value = "Monday" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await response.Content.ReadFromJsonAsync<UserSettingDto>();
        dto.Should().NotBeNull();
        dto!.Key.Should().Be("week_start_day");
        dto.Value.Should().Be("Monday");
    }

    [Fact]
    public async Task Upsert_UpdatesExistingSetting()
    {
        await Client.PutAsJsonAsync("/api/settings/week_start_day", new { value = "Monday" });
        var response = await Client.PutAsJsonAsync("/api/settings/week_start_day", new { value = "Sunday" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<UserSettingDto>();
        dto!.Value.Should().Be("Sunday");
    }

    [Fact]
    public async Task GetByKey_AfterUpsert_ReturnsSetting()
    {
        await Client.PutAsJsonAsync("/api/settings/measurement_system", new { value = "imperial" });

        var response = await Client.GetAsync("/api/settings/measurement_system");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<UserSettingDto>();
        dto!.Value.Should().Be("imperial");
    }

    [Fact]
    public async Task GetByKey_WhenNotFound_Returns404()
    {
        var response = await Client.GetAsync("/api/settings/does_not_exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_RemovesSetting()
    {
        await Client.PutAsJsonAsync("/api/settings/to_delete", new { value = "x" });
        var deleteResponse = await Client.DeleteAsync("/api/settings/to_delete");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await Client.GetAsync("/api/settings/to_delete");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAll_AfterMultipleUpserts_ReturnsAllSettings()
    {
        await Client.PutAsJsonAsync("/api/settings/key1", new { value = "val1" });
        await Client.PutAsJsonAsync("/api/settings/key2", new { value = "val2" });

        var response = await Client.GetAsync("/api/settings");
        var dict = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        dict.Should().ContainKey("key1").WhoseValue.Should().Be("val1");
        dict.Should().ContainKey("key2").WhoseValue.Should().Be("val2");
    }
}

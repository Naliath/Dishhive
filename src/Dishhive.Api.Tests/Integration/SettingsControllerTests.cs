using System.Net.Http.Json;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Models.Settings;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

public class SettingsControllerTests : TestBase
{
    [Fact]
    public async Task Default_settings_return_metric_and_freezy_enabled()
    {
        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/settings");
        settings!.MeasurementSystem.Should().Be(MeasurementSystem.Metric);
        settings.FreezyEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Update_measurement_system_persists()
    {
        var resp = await Client.PutAsJsonAsync("/api/settings/measurement-system",
            new UpdateMeasurementSystemDto { MeasurementSystem = MeasurementSystem.Imperial });
        resp.IsSuccessStatusCode.Should().BeTrue();

        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/settings");
        settings!.MeasurementSystem.Should().Be(MeasurementSystem.Imperial);
    }

    [Fact]
    public async Task Update_freezy_enabled_persists()
    {
        await Client.PutAsJsonAsync("/api/settings/freezy-enabled", new UpdateFreezyEnabledDto { Enabled = false });
        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/settings");
        settings!.FreezyEnabled.Should().BeFalse();
    }
}

using Dishhive.Api.Data;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Models.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Produces("application/json")]
public class SettingsController : ControllerBase
{
    private readonly DishhiveDbContext _db;
    private readonly IConfiguration _config;

    public SettingsController(DishhiveDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpGet]
    [ProducesResponseType(typeof(SettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SettingsDto>> Get(CancellationToken ct)
    {
        var settings = await _db.UserSettings.ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        var measurement = ParseMeasurementSystem(settings, _config);
        var freezyEnabled = ParseBool(settings, SettingKeys.FreezyEnabled, _config.GetValue<bool?>("Dishhive:Freezy:Enabled") ?? true);

        return Ok(new SettingsDto(measurement, freezyEnabled));
    }

    [HttpPut("measurement-system")]
    [ProducesResponseType(typeof(SettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SettingsDto>> UpdateMeasurementSystem(UpdateMeasurementSystemDto dto, CancellationToken ct)
    {
        await UpsertAsync(SettingKeys.MeasurementSystem, dto.MeasurementSystem.ToString().ToLowerInvariant(), ct);
        return await Get(ct);
    }

    [HttpPut("freezy-enabled")]
    [ProducesResponseType(typeof(SettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SettingsDto>> UpdateFreezyEnabled(UpdateFreezyEnabledDto dto, CancellationToken ct)
    {
        await UpsertAsync(SettingKeys.FreezyEnabled, dto.Enabled ? "true" : "false", ct);
        return await Get(ct);
    }

    private async Task UpsertAsync(string key, string value, CancellationToken ct)
    {
        var existing = await _db.UserSettings.FindAsync(new object?[] { key }, ct);
        if (existing is null)
        {
            _db.UserSettings.Add(new UserSetting { Key = key, Value = value });
        }
        else
        {
            existing.Value = value;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static MeasurementSystem ParseMeasurementSystem(IReadOnlyDictionary<string, string> settings, IConfiguration config)
    {
        if (settings.TryGetValue(SettingKeys.MeasurementSystem, out var stored)
            && Enum.TryParse<MeasurementSystem>(stored, ignoreCase: true, out var parsed))
        {
            return parsed;
        }
        var fallback = config.GetValue<string>("Dishhive:Defaults:MeasurementSystem") ?? "metric";
        return Enum.TryParse<MeasurementSystem>(fallback, ignoreCase: true, out var defaultValue)
            ? defaultValue
            : MeasurementSystem.Metric;
    }

    private static bool ParseBool(IReadOnlyDictionary<string, string> settings, string key, bool fallback) =>
        settings.TryGetValue(key, out var stored) && bool.TryParse(stored, out var v) ? v : fallback;
}

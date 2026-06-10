using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

/// <summary>
/// Controller for managing user settings and application preferences.
/// Known keys: "measurementSystem" = "metric" | "imperial" (metric is the default
/// when the key is absent; see docs/features/measurement-preferences.md).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    /// <summary>Setting key for the household's measurement system</summary>
    public const string MeasurementSystemKey = "measurementSystem";

    private readonly DishhiveDbContext _context;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(DishhiveDbContext context, ILogger<SettingsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get a setting by key
    /// </summary>
    [HttpGet("{key}")]
    [ProducesResponseType(typeof(UserSettingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserSettingDto>> GetSetting(string key)
    {
        var setting = await _context.UserSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key);

        if (setting == null)
        {
            return NotFound();
        }

        return ToDto(setting);
    }

    /// <summary>
    /// Get all settings
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<UserSettingDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<UserSettingDto>>> GetAllSettings()
    {
        var settings = await _context.UserSettings
            .AsNoTracking()
            .Select(s => ToDto(s))
            .ToListAsync();

        return Ok(settings);
    }

    /// <summary>
    /// Set or update a setting
    /// </summary>
    [HttpPut("{key}")]
    [ProducesResponseType(typeof(UserSettingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UserSettingDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<UserSettingDto>> SetSetting(string key, [FromBody] UpsertUserSettingDto dto)
    {
        var setting = await _context.UserSettings.FindAsync(key);

        if (setting == null)
        {
            setting = new UserSetting
            {
                Key = key,
                Value = dto.Value,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.UserSettings.Add(setting);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created setting {Key}", key);
            return CreatedAtAction(nameof(GetSetting), new { key }, ToDto(setting));
        }

        setting.Value = dto.Value;
        setting.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated setting {Key}", key);
        return ToDto(setting);
    }

    /// <summary>
    /// Delete a setting (the application falls back to its default)
    /// </summary>
    [HttpDelete("{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSetting(string key)
    {
        var setting = await _context.UserSettings.FindAsync(key);

        if (setting == null)
        {
            return NotFound();
        }

        _context.UserSettings.Remove(setting);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted setting {Key}", key);
        return NoContent();
    }

    private static UserSettingDto ToDto(UserSetting setting) => new()
    {
        Key = setting.Key,
        Value = setting.Value,
        CreatedAt = setting.CreatedAt,
        UpdatedAt = setting.UpdatedAt
    };
}

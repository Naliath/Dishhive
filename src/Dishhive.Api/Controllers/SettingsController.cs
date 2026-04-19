using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly DishhiveDbContext _db;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(DishhiveDbContext db, ILogger<SettingsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Get all settings as a flat dictionary.</summary>
    [HttpGet]
    public async Task<ActionResult<Dictionary<string, string>>> GetAll()
    {
        var settings = await _db.UserSettings.AsNoTracking().ToListAsync();
        return Ok(settings.ToDictionary(s => s.Key, s => s.Value));
    }

    /// <summary>Get a single setting by key. Returns the default if not set.</summary>
    [HttpGet("{key}")]
    public async Task<ActionResult<UserSettingDto>> GetByKey(string key)
    {
        var setting = await _db.UserSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key);

        if (setting == null)
            return NotFound(new { key, message = "Setting not found" });

        return Ok(new UserSettingDto(setting.Key, setting.Value, setting.UpdatedAt));
    }

    /// <summary>Set or update a setting (upsert by key).</summary>
    [HttpPut("{key}")]
    public async Task<ActionResult<UserSettingDto>> Upsert(string key, [FromBody] UpsertSettingDto dto)
    {
        var setting = await _db.UserSettings.FirstOrDefaultAsync(s => s.Key == key);

        bool created = false;
        if (setting == null)
        {
            setting = new UserSetting { Key = key, Value = dto.Value };
            _db.UserSettings.Add(setting);
            created = true;
        }
        else
        {
            setting.Value = dto.Value;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("{Action} setting {Key}={Value}", created ? "Created" : "Updated", key, dto.Value);

        var resultDto = new UserSettingDto(setting.Key, setting.Value, setting.UpdatedAt);
        if (created)
            return CreatedAtAction(nameof(GetByKey), new { key }, resultDto);

        return Ok(resultDto);
    }

    /// <summary>Delete a setting.</summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key)
    {
        var setting = await _db.UserSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting == null)
            return NotFound();

        _db.UserSettings.Remove(setting);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record UserSettingDto(string Key, string Value, DateTime UpdatedAt);

public record UpsertSettingDto([Required][MaxLength(500)] string Value);

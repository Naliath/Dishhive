using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Suggestions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

/// <summary>
/// Controller for managing user settings and application preferences.
/// Known keys: <see cref="UserSettingKeys.MeasurementSystem"/> = "metric" | "imperial" (metric is the default
/// when the key is absent; see docs/features/measurement-preferences.md).
/// <see cref="UserSettingKeys.FirstDayOfWeek"/> = "monday" | "sunday" (Monday is the default when the key is
/// absent); used by the week planner and shopping list to anchor the week.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly DishhiveDbContext _context;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(DishhiveDbContext context, ILogger<SettingsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// The editable AI system prompt: the user-tweakable section, the shipped default,
    /// and the protected machinery that is always appended (JSON contract, collection/
    /// source/freezer rules — post-processing depends on those, so they are not editable).
    /// </summary>
    [HttpGet("ai-prompt")]
    [ProducesResponseType(typeof(AiPromptDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiPromptDto>> GetAiPrompt(
        [FromServices] AiPromptService promptService, CancellationToken cancellationToken)
    {
        return Ok(await BuildAiPromptDto(promptService, cancellationToken));
    }

    /// <summary>
    /// Saves the editable AI prompt section (text equal to the default clears the
    /// customization). Because the prompt changes model behavior, a model capability
    /// re-test is started automatically — its verdict tells the user whether their
    /// prompt still produces working suggestions.
    /// </summary>
    [HttpPut("ai-prompt")]
    [ProducesResponseType(typeof(AiPromptDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiPromptDto>> SetAiPrompt(
        UpdateAiPromptDto dto,
        [FromServices] AiPromptService promptService,
        [FromServices] IAiModelCapabilityService aiCapability,
        [FromServices] AiOptions aiOptions,
        CancellationToken cancellationToken)
    {
        var before = await promptService.GetOverrideAsync(cancellationToken);
        await promptService.SetOverrideAsync(dto.EditablePrompt, cancellationToken);
        var after = await promptService.GetOverrideAsync(cancellationToken);

        if (before != after && aiOptions.IsConfigured)
        {
            _logger.LogInformation("AI prompt changed (customized={Customized}); restarting the model capability test", after != null);
            _ = aiCapability.RetestAsync();
        }

        return Ok(await BuildAiPromptDto(promptService, cancellationToken));
    }

    /// <summary>Resets the AI prompt to the shipped default (also restarts the capability test)</summary>
    [HttpDelete("ai-prompt")]
    [ProducesResponseType(typeof(AiPromptDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiPromptDto>> ResetAiPrompt(
        [FromServices] AiPromptService promptService,
        [FromServices] IAiModelCapabilityService aiCapability,
        [FromServices] AiOptions aiOptions,
        CancellationToken cancellationToken)
    {
        var wasCustomized = await promptService.GetOverrideAsync(cancellationToken) != null;
        await promptService.ResetAsync(cancellationToken);

        if (wasCustomized && aiOptions.IsConfigured)
        {
            _logger.LogInformation("AI prompt reset to the default; restarting the model capability test");
            _ = aiCapability.RetestAsync();
        }

        return Ok(await BuildAiPromptDto(promptService, cancellationToken));
    }

    private static async Task<AiPromptDto> BuildAiPromptDto(
        AiPromptService promptService, CancellationToken cancellationToken)
    {
        var overrideText = await promptService.GetOverrideAsync(cancellationToken);
        return new AiPromptDto(
            EditablePrompt: overrideText ?? LlmMealSuggestionService.EditableSystemPromptDefault,
            DefaultPrompt: LlmMealSuggestionService.EditableSystemPromptDefault,
            ProtectedPrompt: LlmMealSuggestionService.ProtectedSystemPrompt,
            IsCustomized: overrideText != null,
            DefaultChangedSinceCustomized: await promptService.DefaultChangedSinceCustomizedAsync(cancellationToken));
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

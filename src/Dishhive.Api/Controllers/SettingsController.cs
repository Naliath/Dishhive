using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Suggestions;
using Dishhive.Api.Services.Localization;
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
    private readonly SupportedLanguageCatalog _languages;

    public SettingsController(DishhiveDbContext context, ILogger<SettingsController> logger,
        SupportedLanguageCatalog languages)
    {
        _context = context;
        _logger = logger;
        _languages = languages;
    }

    [HttpGet("preferences")]
    [ProducesResponseType(typeof(UserPreferencesDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserPreferencesDto>> GetPreferences(CancellationToken cancellationToken)
    {
        var values = await _context.UserSettings.AsNoTracking()
            .Where(setting => setting.Key == UserSettingKeys.MeasurementSystem
                || setting.Key == UserSettingKeys.FirstDayOfWeek
                || setting.Key == UserSettingKeys.PreferredLanguage
                || setting.Key == UserSettingKeys.TranslateImportedRecipes)
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);
        return BuildPreferences(values);
    }

    [HttpPatch("preferences")]
    [ProducesResponseType(typeof(UserPreferencesDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserPreferencesDto>> UpdatePreference(
        UpdateUserPreferencesDto dto, CancellationToken cancellationToken)
    {
        var updates = new List<(string Key, string Value)>();
        if (dto.MeasurementSystem is { } measurementSystem)
            updates.Add((UserSettingKeys.MeasurementSystem,
                EnumNames.ToName(measurementSystem).ToLowerInvariant()));
        if (dto.FirstDayOfWeek is { } firstDayOfWeek)
            updates.Add((UserSettingKeys.FirstDayOfWeek,
                EnumNames.ToName(firstDayOfWeek).ToLowerInvariant()));
        if (dto.PreferredLanguage is { } preferredLanguage)
            updates.Add((UserSettingKeys.PreferredLanguage, preferredLanguage.ToLowerInvariant()));
        if (dto.TranslateImportedRecipes is { } translateImportedRecipes)
            updates.Add((UserSettingKeys.TranslateImportedRecipes,
                translateImportedRecipes.ToString().ToLowerInvariant()));

        if (updates.Count != 1)
        {
            return ValidationProblem("Supply exactly one preference to update.");
        }
        var (key, value) = updates[0];
        if (!IsValidKnownValue(key, value))
        {
            return ValidationProblem($"Unsupported value '{value}' for setting '{key}'.");
        }

        await UpsertSettingAsync(key, value, cancellationToken);
        return await GetPreferences(cancellationToken);
    }

    private UserPreferencesDto BuildPreferences(IReadOnlyDictionary<string, string> values)
    {
        var defaultLanguage = _languages.DefaultCode;
        var language = values.GetValueOrDefault(UserSettingKeys.PreferredLanguage);
        return new(
            EnumNames.TryParse<MeasurementSystem>(values.GetValueOrDefault(UserSettingKeys.MeasurementSystem), out var measurement)
                ? measurement : MeasurementSystem.Metric,
            EnumNames.TryParse<FirstDayOfWeek>(values.GetValueOrDefault(UserSettingKeys.FirstDayOfWeek), out var firstDay)
                ? firstDay : FirstDayOfWeek.Monday,
            _languages.Contains(language) ? language!.ToLowerInvariant() : defaultLanguage,
            bool.TryParse(values.GetValueOrDefault(UserSettingKeys.TranslateImportedRecipes), out var translate) && translate,
            _languages.Languages.Select(language => new SupportedLanguageDto(language.Code, language.DisplayName)).ToList());
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
            EditablePrompt: overrideText ?? MealSuggestionPromptBuilder.EditableSystemPromptDefault,
            DefaultPrompt: MealSuggestionPromptBuilder.EditableSystemPromptDefault,
            ProtectedPrompt: MealSuggestionPromptBuilder.ProtectedSystemPrompt,
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
    public async Task<ActionResult<UserSettingDto>> SetSetting(
        string key, [FromBody] UpsertUserSettingDto dto, CancellationToken cancellationToken)
    {
        if (!IsValidKnownValue(key, dto.Value))
        {
            return ValidationProblem($"Unsupported value '{dto.Value}' for setting '{key}'.");
        }
        var (setting, created) = await UpsertSettingAsync(key, dto.Value, cancellationToken);
        return created
            ? CreatedAtAction(nameof(GetSetting), new { key }, ToDto(setting))
            : ToDto(setting);
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

    private async Task<(UserSetting Setting, bool Created)> UpsertSettingAsync(
        string key, string value, CancellationToken cancellationToken)
    {
        var setting = await _context.UserSettings.FindAsync([key], cancellationToken);
        var created = setting == null;
        if (setting == null)
        {
            setting = new UserSetting { Key = key, Value = value };
            _context.UserSettings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("{Action} setting {Key}", created ? "Created" : "Updated", key);
        return (setting, created);
    }

    private bool IsValidKnownValue(string key, string value) => key switch
    {
        UserSettingKeys.PreferredLanguage => _languages.Contains(value),
        UserSettingKeys.TranslateImportedRecipes => bool.TryParse(value, out _),
        UserSettingKeys.MeasurementSystem => EnumNames.TryParse<MeasurementSystem>(value, out _),
        UserSettingKeys.FirstDayOfWeek => EnumNames.TryParse<FirstDayOfWeek>(value, out _),
        _ => true
    };
}

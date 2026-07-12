using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.DTOs;

public record SupportedLanguageDto(string Code, string DisplayName);

public record UserPreferencesDto(
    MeasurementSystem MeasurementSystem,
    FirstDayOfWeek FirstDayOfWeek,
    string PreferredLanguage,
    bool TranslateImportedRecipes,
    IReadOnlyList<SupportedLanguageDto> SupportedLanguages);

public record UpdateUserPreferencesDto(
    MeasurementSystem? MeasurementSystem = null,
    FirstDayOfWeek? FirstDayOfWeek = null,
    string? PreferredLanguage = null,
    bool? TranslateImportedRecipes = null);

public class UserSettingDto
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpsertUserSettingDto
{
    [Required]
    [MaxLength(1000)]
    public string Value { get; set; } = string.Empty;
}

/// <summary>The editable AI system prompt and everything the settings page needs to
/// present it honestly: what's editable, what's always appended, and drift state.</summary>
public record AiPromptDto(
    /// <summary>The effective editable section (the user's override, or the default)</summary>
    string EditablePrompt,
    /// <summary>The shipped default for the editable section (reset target)</summary>
    string DefaultPrompt,
    /// <summary>The protected machinery always appended after the editable section</summary>
    string ProtectedPrompt,
    bool IsCustomized,
    /// <summary>The shipped default changed since the user customized — they are on a
    /// fork of an older prompt and may want to reset to pick up the improvements</summary>
    bool DefaultChangedSinceCustomized
);

public class UpdateAiPromptDto
{
    [Required]
    [MaxLength(4000)]
    public string EditablePrompt { get; set; } = string.Empty;
}

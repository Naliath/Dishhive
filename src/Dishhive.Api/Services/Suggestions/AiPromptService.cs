using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>Read access to the editable system-prompt override, for the suggestion
/// pipeline and the capability test (they only need the effective text).</summary>
public interface IAiPromptProvider
{
    /// <summary>The stored editable-prompt override, or null when the default applies</summary>
    Task<string?> GetOverrideAsync(CancellationToken cancellationToken = default);

    /// <summary>The full effective system prompt (override or default + protected rules)</summary>
    async Task<string> GetEffectiveSystemPromptAsync(CancellationToken cancellationToken = default)
        => LlmMealSuggestionService.ComposeSystemPrompt(await GetOverrideAsync(cancellationToken));
}

/// <summary>
/// Stores the user's editable system-prompt section (UserSettings key-value rows).
/// Alongside the override it records the shipped default it was forked from, so the
/// settings page can tell the user when a newer app version improved the built-in
/// prompt they are no longer receiving (see docs/features/ai-week-planning.md).
/// </summary>
public class AiPromptService : IAiPromptProvider
{
    public const int MaxLength = 4000;

    private readonly DishhiveDbContext _context;

    public AiPromptService(DishhiveDbContext context) => _context = context;

    public async Task<string?> GetOverrideAsync(CancellationToken cancellationToken = default)
    {
        var setting = await _context.UserSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == UserSettingKeys.AiSystemPrompt, cancellationToken);
        return string.IsNullOrWhiteSpace(setting?.Value) ? null : setting.Value;
    }

    /// <summary>
    /// Saves the editable prompt. Text identical to the shipped default clears the
    /// customization instead — otherwise the user would be flagged as "customized"
    /// (and frozen off future default improvements) for a no-op edit.
    /// </summary>
    public async Task SetOverrideAsync(string editablePrompt, CancellationToken cancellationToken = default)
    {
        var trimmed = editablePrompt.Trim();
        if (trimmed.Length == 0 || trimmed == LlmMealSuggestionService.EditableSystemPromptDefault)
        {
            await ResetAsync(cancellationToken);
            return;
        }

        await UpsertAsync(UserSettingKeys.AiSystemPrompt, trimmed, cancellationToken);
        await UpsertAsync(
            UserSettingKeys.AiSystemPromptBaseline,
            LlmMealSuggestionService.EditableSystemPromptDefault,
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Back to the shipped default (removes the override and its baseline)</summary>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _context.UserSettings
            .Where(s => s.Key == UserSettingKeys.AiSystemPrompt
                || s.Key == UserSettingKeys.AiSystemPromptBaseline)
            .ToListAsync(cancellationToken);
        if (rows.Count > 0)
        {
            _context.UserSettings.RemoveRange(rows);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// True when the prompt is customized AND the shipped default has changed since —
    /// the user is running on a fork of an older prompt and may want to re-base.
    /// </summary>
    public async Task<bool> DefaultChangedSinceCustomizedAsync(CancellationToken cancellationToken = default)
    {
        var baseline = await _context.UserSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == UserSettingKeys.AiSystemPromptBaseline, cancellationToken);
        return baseline is not null
            && baseline.Value != LlmMealSuggestionService.EditableSystemPromptDefault;
    }

    private async Task UpsertAsync(string key, string value, CancellationToken cancellationToken)
    {
        var setting = await _context.UserSettings.FindAsync([key], cancellationToken);
        if (setting is null)
        {
            _context.UserSettings.Add(new UserSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }
    }
}

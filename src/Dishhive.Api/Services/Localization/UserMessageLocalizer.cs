using System.Reflection;
using System.Text.Json;
using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Localization;

public sealed class UserMessageLocalizer(DishhiveDbContext context)
{
    private const string ResourceMarker = ".Resources.Messages.";
    private static readonly IReadOnlyDictionary<string, JsonElement> Catalogs = LoadCatalogs();
    private string? _language;

    public async Task<string> GetAsync(string key, CancellationToken cancellationToken = default,
        params (string Name, object? Value)[] parameters)
    {
        var language = await GetLanguageAsync(cancellationToken);
        var message = Resolve(language, key) ?? Resolve("en", key) ?? key;
        foreach (var (name, value) in parameters)
            message = message.Replace($"{{{name}}}", Convert.ToString(value) ?? "", StringComparison.Ordinal);
        return message;
    }

    private async Task<string> GetLanguageAsync(CancellationToken cancellationToken)
    {
        if (_language != null) return _language;
        var configured = await context.UserSettings.AsNoTracking()
            .Where(setting => setting.Key == UserSettingKeys.PreferredLanguage)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(cancellationToken);
        _language = configured != null && Catalogs.ContainsKey(configured) ? configured : "en";
        return _language;
    }

    private static string? Resolve(string language, string key)
    {
        if (!Catalogs.TryGetValue(language, out var current)) return null;
        foreach (var segment in key.Split('.'))
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current)) return null;
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static IReadOnlyDictionary<string, JsonElement> LoadCatalogs()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceNames()
            .Where(name => name.Contains(ResourceMarker, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .ToDictionary(
                name => name[(name.IndexOf(ResourceMarker, StringComparison.Ordinal) + ResourceMarker.Length)..^5],
                name => { using var stream = assembly.GetManifestResourceStream(name)!; return JsonDocument.Parse(stream).RootElement.Clone(); },
                StringComparer.OrdinalIgnoreCase);
    }
}

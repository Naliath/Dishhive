using System.Globalization;
using System.Reflection;

namespace Dishhive.Api.Services.Localization;

public sealed record SupportedLanguage(string Code, string DisplayName);

/// <summary>Discovers supported languages from the localization resources shipped by the API.</summary>
public sealed class SupportedLanguageCatalog
{
    private const string ResourceMarker = ".UiTranslations.";

    public IReadOnlyList<SupportedLanguage> Languages { get; } = Assembly.GetExecutingAssembly()
        .GetManifestResourceNames()
        .Where(name => name.Contains(ResourceMarker, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
        .Select(name => name[(name.IndexOf(ResourceMarker, StringComparison.Ordinal) + ResourceMarker.Length)..^5])
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .Select(code => new SupportedLanguage(code, GetDisplayName(code)))
        .ToList();

    public bool Contains(string? code) => Languages.Any(language =>
        string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase));

    public string DefaultCode => Contains("en") ? "en" : Languages.First().Code;

    public SupportedLanguage? Find(string? code) => Languages.FirstOrDefault(language =>
        string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase));

    private static string GetDisplayName(string code)
    {
        try
        {
            var name = CultureInfo.GetCultureInfo(code).NativeName;
            return char.ToUpper(name[0], CultureInfo.GetCultureInfo(code)) + name[1..];
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }
}

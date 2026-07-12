using Dishhive.Api.Services.Localization;

namespace Dishhive.Api.Models;

/// <summary>
/// Resolves user-facing dietary aliases through per-language resource files.
/// Adding a language no longer requires changing application code.
/// </summary>
public static class DietaryTagPresets
{
    public static IReadOnlyList<IngredientClass> Resolve(string name, DietaryTagKind kind)
    {
        var presets = kind == DietaryTagKind.Allergy
            ? LocalizationLexicon.Allergies
            : LocalizationLexicon.Diets;
        return presets.TryGetValue(name.Trim(), out var classes) ? classes : [];
    }
}

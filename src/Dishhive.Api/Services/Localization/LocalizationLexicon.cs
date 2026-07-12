using Dishhive.Api.Models;
using System.Text.Json;

namespace Dishhive.Api.Services.Localization;

internal static class LocalizationLexicon
{
    internal sealed record UnitDefinition(List<string> Aliases, string CanonicalUnit, decimal Factor);
    private sealed record ClassDefinition(List<string> Aliases, List<string> Classes);
    private sealed record Document(List<UnitDefinition> Units, List<ClassDefinition> Allergies, List<ClassDefinition> Diets);

    internal static readonly IReadOnlyDictionary<string, UnitDefinition> Units;
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<IngredientClass>> Allergies;
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<IngredientClass>> Diets;

    static LocalizationLexicon()
    {
        var units = new Dictionary<string, UnitDefinition>(StringComparer.OrdinalIgnoreCase);
        var allergies = new Dictionary<string, IReadOnlyList<IngredientClass>>(StringComparer.OrdinalIgnoreCase);
        var diets = new Dictionary<string, IReadOnlyList<IngredientClass>>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(LocalizationLexicon).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(name => name.Contains(".Resources.Localization.", StringComparison.Ordinal)
                         && name.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing embedded lexicon {resource}");
            var document = JsonSerializer.Deserialize<Document>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException($"Invalid embedded lexicon {resource}");
            foreach (var definition in document.Units)
                foreach (var alias in definition.Aliases) units[alias] = definition;
            AddClasses(document.Allergies, allergies);
            AddClasses(document.Diets, diets);
        }
        Units = units;
        Allergies = allergies;
        Diets = diets;
    }

    private static void AddClasses(
        IEnumerable<ClassDefinition> definitions,
        IDictionary<string, IReadOnlyList<IngredientClass>> target)
    {
        foreach (var definition in definitions)
        {
            var classes = definition.Classes.Select(EnumNames.Parse<IngredientClass>).ToList();
            foreach (var alias in definition.Aliases) target[alias] = classes;
        }
    }
}

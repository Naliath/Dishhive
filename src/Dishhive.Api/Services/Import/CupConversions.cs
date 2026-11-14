using System.Text.Json;
using Dishhive.Api.Models.DTOs;

namespace Dishhive.Api.Services.Import;

public static class CupConversions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public const decimal UsCupMilliliters = 236.5882365m;

    // Small, editable starting list. Weight references: King Arthur Baking's ingredient
    // weight chart (https://www.kingarthurbaking.com/learn/ingredient-weight-chart).
    // Liquids use US customary volume; these are not water-density assumptions.
    public static IReadOnlyList<CupConversionDto> Defaults =>
    [
        new("All-purpose flour", ["flour", "plain flour", "all purpose flour", "bloem", "tarwebloem", "plain flour (all-purpose flour)"], 120, "g"),
        new("Granulated sugar", ["sugar", "white sugar", "suiker", "kristalsuiker"], 198, "g"),
        new("Caster sugar", ["superfine sugar", "castor sugar", "fijne suiker", "caster sugar (superfine sugar)"], 190, "g"),
        new("Packed brown sugar", ["packed light brown sugar", "packed dark brown sugar", "aangedrukte bruine suiker"], 213, "g"),
        new("Powdered sugar", ["icing sugar", "confectioners' sugar", "poedersuiker", "bloemsuiker"], 113, "g"),
        new("Butter", ["unsalted butter", "salted butter", "boter", "ongezouten boter"], 226, "g"),
        new("Rolled oats", ["old-fashioned oats", "quick-cooking oats", "havermout"], 89, "g"),
        new("Almond flour", ["amandelmeel"], 96, "g"),
        new("Water", ["water"], decimal.Round(UsCupMilliliters, 2), "ml"),
        new("Milk", ["whole milk", "melk", "volle melk"], decimal.Round(UsCupMilliliters, 2), "ml"),
        new("Vegetable oil", ["sunflower oil", "plantaardige olie", "zonnebloemolie"], decimal.Round(UsCupMilliliters, 2), "ml"),
        new("Olive oil", ["extra virgin olive oil", "olijfolie"], decimal.Round(UsCupMilliliters, 2), "ml")
    ];

    public static IReadOnlyList<CupConversionDto> Read(string? json) => json is null
        ? Defaults : JsonSerializer.Deserialize<List<CupConversionDto>>(json, JsonOptions) ?? [];

    public static string Write(IReadOnlyList<CupConversionDto> entries) => JsonSerializer.Serialize(entries, JsonOptions);

    public static string? Validate(IReadOnlyList<CupConversionDto> entries)
    {
        if (entries.Count > 100) return "limit";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Ingredient) || entry.Ingredient.Length > 80
                || entry.Quantity is <= 0 or > 10000 || entry.Unit is not ("g" or "ml")
                || entry.Aliases is null || entry.Aliases.Count > 20
                || entry.Aliases.Any(alias => string.IsNullOrWhiteSpace(alias) || alias.Length > 80)) return "invalid";
            var localNames = entry.Aliases.Prepend(entry.Ingredient).Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase);
            if (localNames.Any(name => !names.Add(name))) return "duplicate";
        }
        return Write(entries).Length > 4000 ? "limit" : null;
    }

    private static string Normalize(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

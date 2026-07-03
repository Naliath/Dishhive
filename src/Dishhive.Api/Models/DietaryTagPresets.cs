namespace Dishhive.Api.Models;

/// <summary>
/// Built-in name → excluded-classes presets used to seed
/// <see cref="FamilyMemberDietaryTag.ExcludedClasses"/> when a member gets a tag
/// without an explicit definition. Matching is case-insensitive on the trimmed tag
/// name, Dutch and English synonyms included. An unmapped name yields an empty set:
/// the tag then works exactly like before this feature existed (prompt-only), and
/// the family form marks it as not machine-checkable so the user can define it.
/// </summary>
public static class DietaryTagPresets
{
    private static readonly IngredientClass[] VegetarianSet =
    [
        IngredientClass.RedMeat, IngredientClass.Poultry, IngredientClass.Pork,
        IngredientClass.Fish, IngredientClass.Crustaceans, IngredientClass.Molluscs,
        IngredientClass.Gelatin
    ];

    private static readonly IngredientClass[] VeganSet =
    [
        .. VegetarianSet,
        IngredientClass.Milk, IngredientClass.Eggs, IngredientClass.Honey
    ];

    /// <summary>No meat, fish is fine (also what plain "no meat" means here)</summary>
    private static readonly IngredientClass[] PescatarianSet =
    [
        IngredientClass.RedMeat, IngredientClass.Poultry, IngredientClass.Pork,
        IngredientClass.Gelatin
    ];

    private static readonly Dictionary<string, IngredientClass[]> AllergyPresets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["noten"] = [IngredientClass.TreeNuts],
            ["nuts"] = [IngredientClass.TreeNuts],
            ["tree nuts"] = [IngredientClass.TreeNuts],
            ["pinda"] = [IngredientClass.Peanuts],
            ["pinda's"] = [IngredientClass.Peanuts],
            ["pindas"] = [IngredientClass.Peanuts],
            ["peanut"] = [IngredientClass.Peanuts],
            ["peanuts"] = [IngredientClass.Peanuts],
            ["lactose"] = [IngredientClass.Milk],
            ["melk"] = [IngredientClass.Milk],
            ["milk"] = [IngredientClass.Milk],
            ["zuivel"] = [IngredientClass.Milk],
            ["dairy"] = [IngredientClass.Milk],
            ["gluten"] = [IngredientClass.Gluten],
            ["tarwe"] = [IngredientClass.Gluten],
            ["wheat"] = [IngredientClass.Gluten],
            ["ei"] = [IngredientClass.Eggs],
            ["eieren"] = [IngredientClass.Eggs],
            ["egg"] = [IngredientClass.Eggs],
            ["eggs"] = [IngredientClass.Eggs],
            ["vis"] = [IngredientClass.Fish],
            ["fish"] = [IngredientClass.Fish],
            ["schaaldieren"] = [IngredientClass.Crustaceans],
            ["crustaceans"] = [IngredientClass.Crustaceans],
            ["schelpdieren"] = [IngredientClass.Molluscs],
            ["weekdieren"] = [IngredientClass.Molluscs],
            ["molluscs"] = [IngredientClass.Molluscs],
            ["shellfish"] = [IngredientClass.Crustaceans, IngredientClass.Molluscs],
            ["schaal- en schelpdieren"] = [IngredientClass.Crustaceans, IngredientClass.Molluscs],
            ["soja"] = [IngredientClass.Soybeans],
            ["soy"] = [IngredientClass.Soybeans],
            ["soya"] = [IngredientClass.Soybeans],
            ["sesam"] = [IngredientClass.Sesame],
            ["sesamzaad"] = [IngredientClass.Sesame],
            ["sesame"] = [IngredientClass.Sesame],
            ["mosterd"] = [IngredientClass.Mustard],
            ["mustard"] = [IngredientClass.Mustard],
            ["selderij"] = [IngredientClass.Celery],
            ["selder"] = [IngredientClass.Celery],
            ["celery"] = [IngredientClass.Celery],
            ["sulfiet"] = [IngredientClass.Sulphites],
            ["sulfieten"] = [IngredientClass.Sulphites],
            ["sulphites"] = [IngredientClass.Sulphites],
            ["sulfites"] = [IngredientClass.Sulphites],
            ["lupine"] = [IngredientClass.Lupin],
            ["lupin"] = [IngredientClass.Lupin]
        };

    private static readonly Dictionary<string, IngredientClass[]> DietPresets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["vegetarisch"] = VegetarianSet,
            ["vegetariër"] = VegetarianSet,
            ["vegetarier"] = VegetarianSet,
            ["vegetarian"] = VegetarianSet,
            ["veggie"] = VegetarianSet,
            ["vegan"] = VeganSet,
            ["veganistisch"] = VeganSet,
            ["veganist"] = VeganSet,
            ["pescotarisch"] = PescatarianSet,
            ["pescetarisch"] = PescatarianSet,
            ["pescatarian"] = PescatarianSet,
            ["pescetarian"] = PescatarianSet,
            ["geen vlees"] = PescatarianSet,
            ["no meat"] = PescatarianSet,
            // Pork counts as red meat colloquially; our taxonomy splits it, so
            // "no red meat" must exclude both
            ["geen rood vlees"] = [IngredientClass.RedMeat, IngredientClass.Pork],
            ["no red meat"] = [IngredientClass.RedMeat, IngredientClass.Pork],
            ["geen varkensvlees"] = [IngredientClass.Pork],
            ["geen varken"] = [IngredientClass.Pork],
            ["no pork"] = [IngredientClass.Pork],
            ["varkensvrij"] = [IngredientClass.Pork],
            // Sourcing/certification rules (slaughter, kashrut) are NOT expressible as
            // ingredient classes; this only covers the ingredient-level part of halal
            ["halal"] = [IngredientClass.Pork, IngredientClass.Alcohol, IngredientClass.Gelatin],
            ["geen alcohol"] = [IngredientClass.Alcohol],
            ["no alcohol"] = [IngredientClass.Alcohol],
            ["alcoholvrij"] = [IngredientClass.Alcohol]
        };

    /// <summary>
    /// The preset excluded classes for a tag name of the given kind; empty when the
    /// name has no built-in definition.
    /// </summary>
    public static IReadOnlyList<IngredientClass> Resolve(string name, DietaryTagKind kind)
    {
        var presets = kind == DietaryTagKind.Allergy ? AllergyPresets : DietPresets;
        return presets.TryGetValue(name.Trim(), out var classes) ? classes : [];
    }
}

namespace Dishhive.Api.Models;

/// <summary>
/// Canonical ingredient classes a recipe can contain: the EU-14 allergen list plus
/// diet-relevant classes (meat split three ways, gelatin, alcohol, honey). Recipes
/// record which classes they contain as objective facts (<see cref="RecipeDietaryFact"/>);
/// family members record which classes their dietary tags exclude
/// (<see cref="FamilyMemberDietaryTag.ExcludedClasses"/>) — so "vegetarian" stays a
/// person-side policy over these facts, never a recipe-side verdict. Values are
/// persisted by name (CSV) and by int (fact rows): never renumber or rename.
/// </summary>
public enum IngredientClass
{
    // EU-14 allergens
    Gluten = 0,
    Crustaceans = 1,
    Eggs = 2,
    Fish = 3,
    Peanuts = 4,
    Soybeans = 5,
    Milk = 6,
    TreeNuts = 7,
    Celery = 8,
    Mustard = 9,
    Sesame = 10,
    Sulphites = 11,
    Lupin = 12,
    Molluscs = 13,

    // Meat, split so "no pork" and "no meat but poultry is fine" are expressible
    RedMeat = 14,
    Poultry = 15,
    Pork = 16,

    // Diet-relevant non-allergens (vegetarian/vegan/halal-style exclusions)
    Gelatin = 17,
    Alcohol = 18,
    Honey = 19
}

/// <summary>Name-based (de)serialization helpers for <see cref="IngredientClass"/>;
/// DTOs carry class names, never ints, so payloads stay self-describing</summary>
public static class IngredientClasses
{
    /// <summary>Case-insensitive name parse; rejects undefined values (including
    /// the numeric strings Enum.TryParse would otherwise accept)</summary>
    public static bool TryParse(string? name, out IngredientClass value)
    {
        return EnumNames.TryParse(name, out value);
    }

    /// <summary>Distinct, enum-ordered names for DTOs</summary>
    public static List<string> ToNames(IEnumerable<IngredientClass> classes) => classes
        .Distinct()
        .OrderBy(c => c)
        .Select(EnumNames.ToName)
        .ToList();
}

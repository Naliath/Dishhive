using System.Text.Json;

namespace Dishhive.Api.Services.Demo;

/// <summary>
/// A demo collection; members reference entries of <see cref="DemoData.RecipeUrls"/>
/// so they link to the seeded recipes.
/// </summary>
public record DemoCollection(string Name, IReadOnlyList<string> RecipeUrls);

/// <summary>
/// A demo household member; favorite recipes reference entries of
/// <see cref="DemoData.RecipeUrls"/> so they link to the imported recipes.
/// </summary>
public record DemoMember(
    string Name,
    IReadOnlyList<string> AllergyTags,
    IReadOnlyList<string> DietTags,
    string? PreferenceNotes,
    IReadOnlyList<string> FavoriteRecipeUrls,
    IReadOnlyList<string> FavoriteDishNames);

/// <summary>
/// Curated offline recipe selection and the crew of the Rocinante (The Expanse).
/// </summary>
public static class DemoData
{
    private const string Base = "https://dagelijksekost.vrt.be/gerechten/";

    /// <summary>
    /// Recipes collected through the regular importer and enrichment queue before
    /// being exported into the embedded demo ZIP. Runtime seeding is offline.
    /// </summary>
    public static readonly IReadOnlyList<string> RecipeUrls = LoadSelection().Select(s => s.Url).ToArray();

    private static DemoRecipeSelection[] LoadSelection()
    {
        var assembly = typeof(DemoData).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Dishhive.Api.Services.Demo.demo-recipes.json")
            ?? throw new InvalidOperationException("Embedded demo recipe selection not found.");
        return JsonSerializer.Deserialize<DemoRecipeSelection[]>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The demo recipe selection is empty.");
    }

    /// <summary>
    /// Demo collections over the seeded recipes, so the collections feature (and the
    /// #[Name] planning references) can be tried straight away.
    /// </summary>
    public static readonly IReadOnlyList<DemoCollection> Collections =
    [
        new DemoCollection("Easy Weekday Dishes",
        [
            Base + "pasta-half-en-half",
            Base + "kip-in-romige-tomatensaus-met-gebakken-aardappelen-en-sla",
            Base + "courgette-fetaburger-tomatensla-rode-ui-orzo",
            Base + "pad-khee-mao-drunken-noodles",
            Base + "loaded-fries-pulled-chicken-jalapenomaiyonaise"
        ]),

        new DemoCollection("Comfort Food",
        [
            Base + "lasagne-verde",
            Base + "kiprollade-ei-champignonsaus-kroketten-kropsla",
            Base + "lamsschenkel-bier-asperges-geroosterde-aardappelen",
            Base + "loaded-fries-pulled-chicken-jalapenomaiyonaise"
        ]),

        new DemoCollection("Feestelijk",
        [
            Base + "gambas-saffraanrijst-salsa-pimiento-del-piquillo",
            Base + "kalfsoester-looksaus-broccolini-puree",
            Base + "kabeljauw-met-aspergepuree-en-hollandaise-met-daslook",
            Base + "cheesecake-met-aardbeien-en-coulis-van-aardbei",
            Base + "eton-mess-frambozen-violette"
        ])
    ];

    /// <summary>
    /// The Rocinante crew as demo household: a vegetarian, two members with
    /// allergies/intolerances and a variety of favorite dishes.
    /// </summary>
    public static readonly IReadOnlyList<DemoMember> Members =
    [
        new DemoMember(
            Name: "James Holden",
            AllergyTags: ["Shellfish"],
            DietTags: [],
            PreferenceNotes: "Runs on coffee; happiest with a simple, honest plate of pasta",
            FavoriteRecipeUrls:
            [
                Base + "pasta-half-en-half",
                Base + "kip-in-romige-tomatensaus-met-gebakken-aardappelen-en-sla"
            ],
            FavoriteDishNames: ["Spaghetti bolognese"]),

        new DemoMember(
            Name: "Naomi Nagata",
            AllergyTags: [],
            DietTags: ["Vegetarian"],
            PreferenceNotes: "Prefers plant-based meals; loves noodles and anything with fresh vegetables",
            FavoriteRecipeUrls:
            [
                Base + "bloemkool-in-zoetzure-saus-met-groenten-en-rijst",
                Base + "courgette-fetaburger-tomatensla-rode-ui-orzo",
                Base + "aardappelballetjes-gevuld-met-scamorza-botersaus-met-salie-en-broccolini"
            ],
            FavoriteDishNames: ["Vegetable curry"]),

        new DemoMember(
            Name: "Alex Kamal",
            AllergyTags: ["Lactose"],
            DietTags: [],
            PreferenceNotes: "Loves spicy food and is famous for his lasagna",
            FavoriteRecipeUrls:
            [
                Base + "lasagne-verde",
                Base + "pad-khee-mao-drunken-noodles"
            ],
            FavoriteDishNames: ["Chili con carne"]),

        new DemoMember(
            Name: "Amos Burton",
            AllergyTags: [],
            DietTags: [],
            PreferenceNotes: "Eats anything, prefers hearty meals and big portions",
            FavoriteRecipeUrls:
            [
                Base + "loaded-fries-pulled-chicken-jalapenomaiyonaise",
                Base + "lamsschenkel-bier-asperges-geroosterde-aardappelen"
            ],
            FavoriteDishNames: ["Cheeseburger and fries"])
    ];
}

namespace Dishhive.Api.Services.Demo;

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
/// Static demo dataset: 20 recipes scraped live from Dagelijkse Kost at seed time,
/// and the crew of the Rocinante (The Expanse) as the household.
/// </summary>
public static class DemoData
{
    private const string Base = "https://dagelijksekost.vrt.be/gerechten/";

    /// <summary>
    /// 20 Dagelijkse Kost recipe URLs with a spread of meat, fish, vegetarian
    /// dishes, lunches and desserts. Imported through the regular recipe import
    /// pipeline so they get ingredients, steps and locally stored images.
    /// </summary>
    public static readonly IReadOnlyList<string> RecipeUrls =
    [
        Base + "lasagne-verde",
        Base + "courgette-fetaburger-tomatensla-rode-ui-orzo",
        Base + "bloemkool-in-zoetzure-saus-met-groenten-en-rijst",
        Base + "gegrilde-gegratineerde-artisjok-tomatencompote-peterseliepesto",
        Base + "aardappelballetjes-gevuld-met-scamorza-botersaus-met-salie-en-broccolini",
        Base + "kalfsoester-looksaus-broccolini-puree",
        Base + "iga-bakar-rijst-boontjes-atjar",
        Base + "kiprollade-ei-champignonsaus-kroketten-kropsla",
        Base + "kip-in-romige-tomatensaus-met-gebakken-aardappelen-en-sla",
        Base + "lamsschenkel-bier-asperges-geroosterde-aardappelen",
        Base + "pad-khee-mao-drunken-noodles",
        Base + "pasta-half-en-half",
        Base + "loaded-fries-pulled-chicken-jalapenomaiyonaise",
        Base + "gambas-saffraanrijst-salsa-pimiento-del-piquillo",
        Base + "kabeljauw-met-aspergepuree-en-hollandaise-met-daslook",
        Base + "zalmballetjes-met-avocadolabneh-en-kruidensalade",
        Base + "komkommersandwich-geitenkaas-gerookte-zalm",
        Base + "cheesecake-met-aardbeien-en-coulis-van-aardbei",
        Base + "havermoutcrumble-rabarber-gember",
        Base + "eton-mess-frambozen-violette"
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

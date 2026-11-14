using System.Text.Json;
using Dishhive.Api.Services.Import;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// Validates recipe extraction from Dagelijkse Kost against stored HTML fixtures of real
/// recipe pages, including both the legacy instruction map and current recipe-parts format.
/// These tests are offline; add a fresh fixture when investigating a site format change.
/// </summary>
public class DagelijkseKostProviderTests
{
    private const string FixtureSourceUrl =
        "https://dagelijksekost.vrt.be/gerechten/cremeux-citroen-bodem-witte-chocolade-gepofte-rijst-rode-bessen";

    private readonly DagelijkseKostProvider _provider = new();

    private static string LoadFixture(string name = "dagelijkse-kost-recipe.html")
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        return File.ReadAllText(path);
    }

    private async Task<ImportedRecipe> ExtractFixtureAsync()
    {
        return await _provider.ExtractAsync(LoadFixture(), new Uri(FixtureSourceUrl));
    }

    [Fact]
    public void CanHandle_DagelijkseKostUrl_ReturnsTrue()
    {
        _provider.CanHandle(new Uri(FixtureSourceUrl)).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_OtherDomain_ReturnsFalse()
    {
        _provider.CanHandle(new Uri("https://example.com/gerechten/iets")).Should().BeFalse();
    }

    [Fact]
    public async Task Extract_FixturePage_ReturnsTitle()
    {
        // The og:title carries the clean dish name; the JSON-LD "name" on this site
        // duplicates the SEO description sentence ("Jeroen Meus maakt ...")
        var recipe = await ExtractFixtureAsync();

        recipe.Title.Should().StartWith("Crémeux van citroen");
        recipe.Title.Should().NotContain("Jeroen Meus");
    }

    [Fact]
    public async Task Extract_PageWithoutOgTitle_FallsBackToJsonLdName()
    {
        const string html = """
            <html><head><script type="application/ld+json">
            {
              "@context": "https://schema.org",
              "@type": "Recipe",
              "name": "Fallbackgerecht",
              "recipeIngredient": ["1 ei"],
              "recipeInstructions": [{"@type": "HowToStep", "text": "Kook het ei."}]
            }
            </script></head><body></body></html>
            """;

        var recipe = await _provider.ExtractAsync(
            html, new Uri("https://dagelijksekost.vrt.be/gerechten/fallbackgerecht"));

        recipe.Title.Should().Be("Fallbackgerecht");
    }

    [Fact]
    public async Task Extract_FixturePage_ReturnsDescription()
    {
        var recipe = await ExtractFixtureAsync();

        recipe.Description.Should().NotBeNullOrWhiteSpace();
        recipe.Description.Should().Contain("witte chocolade");
    }

    [Fact]
    public async Task Extract_FixturePage_ReturnsAllIngredients()
    {
        var recipe = await ExtractFixtureAsync();

        recipe.IngredientLines.Should().HaveCount(15);
        recipe.IngredientLines.Should().Contain("200 gram boter");
        recipe.IngredientLines.Should().Contain("100 gram witte chocolade");
        recipe.IngredientLines.Should().Contain("85 milliliter citroensap");
        recipe.IngredientLines.Should().Contain("Cointreau");
    }

    [Fact]
    public async Task Extract_FixturePage_ReturnsAllSteps()
    {
        // The JSON-LD on this site truncates recipeInstructions to 2 steps; the full list
        // must come from the Next.js page payload (11 steps on the fixture page)
        var recipe = await ExtractFixtureAsync();

        recipe.Steps.Should().HaveCount(11);
        recipe.Steps[0].Should().Be("Laat de gelatine weken in koud water.");
        recipe.Steps[2].Should().StartWith("Knijp de geweekte gelatine uit");
        recipe.Steps[10].Should().Be(
            "Verkruimel er wat van de wafeltjes over. Werk af met rode bessen, braambessen en coulis.");
        recipe.Steps.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s));
    }

    [Fact]
    public async Task Extract_AppeltaartFixture_ReturnsAll23StepsInOrder()
    {
        var recipe = await _provider.ExtractAsync(
            LoadFixture("dagelijkse-kost-appeltaart-met-vanilleroom.html"),
            new Uri("https://dagelijksekost.vrt.be/gerechten/appeltaart-met-vanilleroom"));

        recipe.Title.Should().Be("Appeltaart met vanilleroom");
        recipe.Steps.Should().Equal(
            "Laat de oven voorverwarmen tot 175°C.",
            "Schil de appelen, snij ze in blokjes en stoof ze in boter.",
            "Voeg er de zestes en het sap van de sinaasappel, de peul van een vanillestok ( gebruik, de zaadjes voor de vanilleroom ), de suiker en de kaneelstokjes aan toe.",
            "Zet een deksel op de pot en laat de appel platstoven.",
            "Warm de melk voor het deeg licht op en laat de gist erin oplossen.",
            "Meng het ei met de suiker in de keukenmachine.",
            "Voeg er de melk met de gist aan toe, gevolgd door de bloem met het zout en het bakpoeder en als laatste de boter.",
            "Laat het deeg mengen tot het van de randen van de kom loslaat.",
            "Laat de appelmoes afkoelen.",
            "Vet een taartvorm in en bestuif hem met bloem.",
            "Rol het deeg dun uit en bekleed er de vorm mee.",
            "Snij het overtollige deeg weg en maak met het overschot een nieuwe bol deeg.",
            "Prik gaatjes in de bodem van deeg.",
            "Rol de rest van het deeg ook dun uit.",
            "Schep de afgekoelde appelmoes in de taartvorm en leg er de tweede lap deeg bovenop.",
            "Duw de randen mooi aan en snij het overtollige deeg weg of ga met een deegrol over de randen.",
            "Kluts de dooier met een scheutje melk en wrijf de hele bovenkant van de taart in.",
            "Maak met een mes of een schaar enkele inkepingen in het deksel.",
            "Strooi de suiker over de taart en bak de taart 45 minuten in de oven.",
            "Voeg de zaadjes van de vanillepeul en de bloemsuiker toe aan de slagroom en klop de room half op.",
            "Laat de taart voledig afkoelen.",
            "Serveer de vanilleroom bij een stuk appeltaart.",
            "Werk af met een snuifje kaneelpoeder op de vanilleroom.");
    }

    [Fact]
    public async Task Extract_RecipePartsAcrossFlightChunks_ReturnsStepDescriptionsInPartOrder()
    {
        const string payload = """
            1:{"instructions":{"title":"Instructies"},"recipeParts" : [
              {"title":"De vulling","ingredients":[{"description":"Geen stap"}],"instructions":[
                {"step":1,"description":" Meng \"appel\" met {suiker} [en kaneel]. ","tip":"Geen stap"},
                {"step":2,"description":"Vul de taart."}
              ]},
              {"title":"Afwerking","instructions":[
                {"step":3,"description":"Bak de taart."},
                {"step":4,"description":"Serveer met room."}
              ]}
            ]}
            """;
        var recipe = await ExtractPayloadAsync(payload, payload.IndexOf("suiker", StringComparison.Ordinal));

        recipe.Steps.Should().Equal(
            "Meng \"appel\" met {suiker} [en kaneel].", "Vul de taart.", "Bak de taart.", "Serveer met room.");
    }

    [Fact]
    public async Task Extract_LegacyInstructionsMap_ReturnsStepsInNumericOrder()
    {
        const string payload = """
            1:{"instructions":{"0":"Meng het deeg.","10":"Serveer.","2":"Bak de taart."}}
            """;
        var recipe = await ExtractPayloadAsync(payload, payload.IndexOf("instructions", StringComparison.Ordinal) + 5);

        recipe.Steps.Should().Equal("Meng het deeg.", "Bak de taart.", "Serveer.");
    }

    [Theory]
    [InlineData("1:{\"recipeParts\":[{\"instructions\":[{\"description\":\"Onvolledig\"}")]
    [InlineData("1:{\"recipeParts\":[invalid]}")]
    [InlineData("1:{\"recipeParts\":[]}")]
    [InlineData("1:{\"recipeParts\":[null,{}, {\"instructions\":null}]}")]
    [InlineData("1:{\"recipeParts\":[{\"instructions\":[null,{}, {\"description\":42}, {\"description\":\" \"}]}]}")]
    public async Task Extract_UnusableRecipeParts_FallsBackToJsonLdSteps(string payload)
    {
        var recipe = await ExtractPayloadAsync(payload);

        recipe.Steps.Should().Equal("Kook het ei.", "Pel het ei.");
    }

    [Fact]
    public async Task Extract_RecipePartsWithFewerSteps_KeepsMoreCompleteJsonLd()
    {
        var recipe = await ExtractPayloadAsync("""1:{"recipeParts":[{"instructions":[{"description":"Eén stap."}]}]}""");

        recipe.Steps.Should().Equal("Kook het ei.", "Pel het ei.");
    }

    [Fact]
    public async Task Extract_EmptyRecipePartsWithLegacyMap_FallsBackToLegacySteps()
    {
        const string payload = """
            1:{"recipeParts":[],"instructions":{"0":"Meng het deeg.","1":"Bak de taart.","2":"Serveer."}}
            """;
        var recipe = await ExtractPayloadAsync(payload);

        recipe.Steps.Should().Equal("Meng het deeg.", "Bak de taart.", "Serveer.");
    }

    private Task<ImportedRecipe> ExtractPayloadAsync(string payload, int? splitAt = null)
    {
        const string html = """
            <script type="application/ld+json">
            {"@type":"Recipe","name":"Testgerecht","recipeInstructions":[
              {"@type":"HowToStep","text":"Kook het ei."},
              {"@type":"HowToStep","text":"Pel het ei."}
            ]}
            </script>
            """;
        var chunks = splitAt.HasValue
            ? new[] { payload[..splitAt.Value], payload[splitAt.Value..] }
            : [payload];
        var scripts = string.Concat(chunks.Select(chunk =>
            $"<script>self.__next_f.push([1,{JsonSerializer.Serialize(chunk)}])</script>"));
        return _provider.ExtractAsync(html + scripts, new Uri("https://dagelijksekost.vrt.be/gerechten/testgerecht"));
    }

    [Fact]
    public async Task Extract_PageWithoutNextPayload_FallsBackToJsonLdSteps()
    {
        const string html = """
            <html><head><script type="application/ld+json">
            {
              "@context": "https://schema.org",
              "@type": "Recipe",
              "name": "Fallbackgerecht",
              "recipeIngredient": ["1 ei"],
              "recipeInstructions": [
                {"@type": "HowToStep", "text": "Kook het ei."},
                {"@type": "HowToStep", "text": "Pel het ei."}
              ]
            }
            </script></head><body></body></html>
            """;

        var recipe = await _provider.ExtractAsync(
            html, new Uri("https://dagelijksekost.vrt.be/gerechten/fallbackgerecht"));

        recipe.Steps.Should().Equal("Kook het ei.", "Pel het ei.");
    }

    [Fact]
    public async Task Extract_FixturePage_ReturnsServingCount()
    {
        var recipe = await ExtractFixtureAsync();

        recipe.Servings.Should().Be(4);
    }

    [Fact]
    public async Task Extract_FixturePage_ReturnsPicture()
    {
        var recipe = await ExtractFixtureAsync();

        recipe.ImageUrl.Should().NotBeNullOrWhiteSpace();
        recipe.ImageUrl.Should().StartWith("https://");
        recipe.ImageUrl.Should().Contain("dagelijkse-kost");
    }

    [Fact]
    public async Task Extract_FixturePage_ReturnsSourceLink()
    {
        var recipe = await ExtractFixtureAsync();

        recipe.SourceUrl.Should().Be(FixtureSourceUrl);
    }

    [Fact]
    public async Task Extract_FixturePage_VideoAbsent_ReturnsNullVideoLink()
    {
        // The captured page publishes no video in its JSON-LD; the mapping must yield null
        var recipe = await ExtractFixtureAsync();

        recipe.VideoUrl.Should().BeNull();
    }

    [Fact]
    public async Task Extract_FixturePage_ReturnsTimesAndMetadata()
    {
        var recipe = await ExtractFixtureAsync();

        recipe.TotalTimeMinutes.Should().Be(45);
        recipe.PrepTimeMinutes.Should().Be(30);
        recipe.CookTimeMinutes.Should().Be(15);
        recipe.Category.Should().Be("Dessert");
        recipe.Keywords.Should().Contain("Zomer");
    }

    [Fact]
    public async Task Extract_FixturePage_PreservesRawSourceData()
    {
        var recipe = await ExtractFixtureAsync();

        recipe.RawData.Should().NotBeNullOrWhiteSpace();
        recipe.RawData.Should().Contain("\"@type\":\"Recipe\"");
    }

    [Fact]
    public async Task Extract_RecipeWithVideo_ReturnsVideoLink()
    {
        // schema.org video is optional on Dagelijkse Kost pages; validate the mapping
        // with a minimal page that includes a VideoObject
        const string html = """
            <html><head><script type="application/ld+json">
            {
              "@context": "https://schema.org",
              "@type": "Recipe",
              "name": "Testgerecht",
              "recipeIngredient": ["1 ei"],
              "recipeInstructions": [{"@type": "HowToStep", "text": "Kook het ei."}],
              "recipeYield": "2",
              "video": {
                "@type": "VideoObject",
                "name": "Testgerecht video",
                "contentUrl": "https://dagelijksekost.vrt.be/videos/testgerecht.mp4"
              }
            }
            </script></head><body></body></html>
            """;

        var recipe = await _provider.ExtractAsync(
            html, new Uri("https://dagelijksekost.vrt.be/gerechten/testgerecht"));

        recipe.VideoUrl.Should().Be("https://dagelijksekost.vrt.be/videos/testgerecht.mp4");
        recipe.Servings.Should().Be(2);
    }

    [Fact]
    public async Task Extract_PageWithoutRecipeData_ThrowsExtractionFailed()
    {
        const string html = "<html><head><title>Geen recept</title></head><body></body></html>";

        var act = () => _provider.ExtractAsync(html, new Uri("https://dagelijksekost.vrt.be/over-ons"));

        await act.Should().ThrowAsync<RecipeExtractionFailedException>();
    }
}

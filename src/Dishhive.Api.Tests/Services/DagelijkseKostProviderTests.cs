using Dishhive.Api.Services.Import;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// Validates recipe extraction from Dagelijkse Kost against a stored HTML fixture of a real
/// recipe page (Fixtures/dagelijkse-kost-recipe.html, captured June 10, 2026).
/// These tests are offline: if the site changes its JSON-LD structure, re-capture the fixture
/// and these tests will report exactly which fields broke.
/// </summary>
public class DagelijkseKostProviderTests
{
    private const string FixtureSourceUrl =
        "https://dagelijksekost.vrt.be/gerechten/cremeux-citroen-bodem-witte-chocolade-gepofte-rijst-rode-bessen";

    private readonly DagelijkseKostProvider _provider = new();

    private static string LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "dagelijkse-kost-recipe.html");
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
        var recipe = await ExtractFixtureAsync();

        recipe.Title.Should().Contain("crémeux van citroen");
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

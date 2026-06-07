using Dishhive.Api.Services.RecipeImport.Providers;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// Validates the DagelijkseKost recipe import provider against a captured fixture.
/// This is the test required by the recipe-import feature contract: it confirms that
/// for a known input the provider produces a recipe with all expected fields populated.
/// No outbound HTTP — fixture-driven.
/// </summary>
public class DagelijkseKostRecipeProviderTests
{
    private static string LoadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static DagelijkseKostRecipeProvider CreateSut() =>
        new(new HttpClient { BaseAddress = new Uri("https://dagelijksekost.vrt.be/") });

    [Fact]
    public void CanHandle_returns_true_for_dagelijksekost_urls()
    {
        var sut = CreateSut();

        sut.CanHandle(new Uri("https://dagelijksekost.vrt.be/gerechten/foo")).Should().BeTrue();
        sut.CanHandle(new Uri("https://www.dagelijksekost.vrt.be/gerechten/foo")).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_returns_false_for_other_hosts()
    {
        var sut = CreateSut();
        sut.CanHandle(new Uri("https://example.com/recipes/foo")).Should().BeFalse();
    }

    [Fact]
    public void Parse_extracts_all_required_fields_from_jsonld_fixture()
    {
        var html = LoadFixture("dagelijksekost-roerei-asperges-zalm.html");
        var sourceUrl = new Uri("https://dagelijksekost.vrt.be/gerechten/roerei-asperges-gerookte-zalm");
        var sut = CreateSut();

        var result = sut.Parse(sourceUrl, html);

        // Title
        result.Title.Should().Be("Roerei met asperges en gerookte zalm");

        // Description
        result.Description.Should().Contain("Roerei met gepocheerde witte asperges");

        // Servings
        result.Servings.Should().Be(4);

        // Picture
        result.ImageUrl.Should().StartWith("https://cdn.dagelijksekost.tv/recipes/");

        // Video link
        result.VideoUrl.Should().Be("https://video.dagelijksekost.tv/roerei-asperges-zalm.mp4");

        // Source link
        result.SourceUrl.Should().Be(sourceUrl);
        result.ProviderKey.Should().Be("dagelijksekost");

        // Ingredients
        result.Ingredients.Should().HaveCount(11);
        var asparagus = result.Ingredients[0];
        asparagus.Name.Should().Contain("asperges");
        asparagus.Quantity.Should().Be(500m);
        asparagus.Unit.Should().Be("g");
        asparagus.OriginalQuantity.Should().Be(500m);
        asparagus.OriginalUnit.Should().Be("g");

        var eggs = result.Ingredients.Single(i => i.Name.Contains("eieren", StringComparison.OrdinalIgnoreCase));
        eggs.Quantity.Should().Be(4m);

        // Steps
        result.Steps.Should().HaveCount(5);
        result.Steps[0].Order.Should().Be(0);
        result.Steps[0].Text.Should().Contain("Schil de asperges");
        result.Steps[^1].Text.Should().Contain("citroensap");

        // Tags (keywords + category + cuisine)
        result.Tags.Should().Contain(new[] { "asperges", "zalm", "Hoofdgerecht", "Belgisch" });

        // Source-specific raw payload preserved
        result.SourceRawPayload.Should().NotBeNullOrWhiteSpace();
        result.SourceRawPayload.Should().Contain("\"@type\"");
        result.SourceRawPayload.Should().Contain("Recipe");
    }

    [Fact]
    public void ParseIngredientText_handles_typical_formats()
    {
        var (qty, unit, name) = DagelijkseKostRecipeProvider.ParseIngredientText("500 g witte asperges");
        qty.Should().Be(500m);
        unit.Should().Be("g");
        name.Should().Be("witte asperges");

        var plain = DagelijkseKostRecipeProvider.ParseIngredientText("4 eieren");
        plain.qty.Should().Be(4m);
        plain.unit.Should().BeNull();
        plain.name.Should().Be("eieren");

        var noQty = DagelijkseKostRecipeProvider.ParseIngredientText("snuifje peper");
        noQty.qty.Should().BeNull();
        noQty.name.Should().Be("snuifje peper");
    }

    [Fact]
    public void Parse_uses_full_instructions_from_page_data_when_jsonld_is_truncated()
    {
        // Real-world page where the JSON-LD `recipeInstructions` array contains only
        // 2 of the 16 steps; the full ordered list lives in the embedded Next.js data.
        var html = LoadFixture("dagelijksekost-kotelet-wortelen.html");
        var sourceUrl = new Uri("https://dagelijksekost.vrt.be/gerechten/kotelet-jonge-wortelen-puree-tuinkers-tijmsaus");
        var sut = CreateSut();

        var result = sut.Parse(sourceUrl, html);

        result.Steps.Should().HaveCount(16);
        result.Steps[0].Order.Should().Be(0);
        result.Steps[0].Text.Should().StartWith("Snij de aardappelen");
        result.Steps[2].Text.Should().Contain("tuinkers erdoorheen");
        result.Steps[^1].Text.Should().Contain("kotelet");
    }
}

using Dishhive.Api.Services.Agents.RecipeImport;
using FluentAssertions;
using HtmlAgilityPack;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// Validates that the JSON-LD parser, extracted from the DagelijkseKost provider for reuse
/// by the learned-source provider and recipe-import agent, produces the same fields against
/// the same fixture.
/// </summary>
public class JsonLdRecipeParserTests
{
    private static string LoadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void TryParse_extracts_recipe_from_jsonld_fixture()
    {
        var html = LoadFixture("dagelijksekost-roerei-asperges-zalm.html");
        var url = new Uri("https://dagelijksekost.vrt.be/gerechten/roerei-asperges-gerookte-zalm");

        var result = JsonLdRecipeParser.TryParse(url, html, providerKey: "learned:dagelijksekost.vrt.be");

        result.Should().NotBeNull();
        result!.Title.Should().Be("Roerei met asperges en gerookte zalm");
        result.Servings.Should().Be(4);
        result.Ingredients.Should().NotBeEmpty();
        result.Steps.Should().NotBeEmpty();
        result.ProviderKey.Should().Be("learned:dagelijksekost.vrt.be");
        result.SourceUrl.Should().Be(url);
    }

    [Fact]
    public void TryParse_returns_null_when_no_jsonld_recipe_present()
    {
        var html = "<html><body><h1>not a recipe</h1></body></html>";
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var result = JsonLdRecipeParser.TryParse(
            new Uri("https://example.com/foo"), doc, "learned:example.com");

        result.Should().BeNull();
    }
}

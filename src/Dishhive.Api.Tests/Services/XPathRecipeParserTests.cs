using Dishhive.Api.Models.Agents;
using Dishhive.Api.Services.Agents.RecipeImport;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// Validates the deterministic XPath replay parser used by the learned-source provider
/// when an LLM previously emitted XPath selectors for a non-JSON-LD source.
/// </summary>
public class XPathRecipeParserTests
{
    private const string Html = """
        <html><body>
          <h1 class="recipe-title">Test Pancakes</h1>
          <p class="desc">Fluffy pancakes for a Sunday morning.</p>
          <span class="serves">4</span>
          <ul class="ingredients">
            <li>200 g flour</li>
            <li>2 eggs</li>
            <li>300 ml milk</li>
          </ul>
          <ol class="steps">
            <li>Mix the dry ingredients.</li>
            <li>Whisk in the wet ingredients.</li>
            <li>Cook on a hot pan.</li>
          </ol>
        </body></html>
        """;

    [Fact]
    public void TryParse_extracts_title_servings_ingredients_and_steps_via_xpath()
    {
        var blueprint = new RecipeImportBlueprint
        {
            Strategy = LearnedRecipeSourceStrategy.XPath,
            TitleXPath = "//h1[@class='recipe-title']",
            DescriptionXPath = "//p[@class='desc']",
            ServingsXPath = "//span[@class='serves']",
            IngredientsXPath = "//ul[@class='ingredients']/li",
            StepsXPath = "//ol[@class='steps']/li",
        };

        var result = XPathRecipeParser.TryParse(
            new Uri("https://example.com/pancakes"), Html, providerKey: "learned:example.com", blueprint);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Test Pancakes");
        result.Description.Should().Contain("Fluffy");
        result.Servings.Should().Be(4);
        result.Ingredients.Should().HaveCount(3);
        result.Steps.Should().HaveCount(3);
        result.Steps[0].Order.Should().Be(0);
    }

    [Fact]
    public void TryParse_returns_null_when_title_missing()
    {
        var blueprint = new RecipeImportBlueprint
        {
            Strategy = LearnedRecipeSourceStrategy.XPath,
            TitleXPath = "//h1[@class='does-not-exist']",
            IngredientsXPath = "//ul[@class='ingredients']/li",
            StepsXPath = "//ol[@class='steps']/li",
        };

        var result = XPathRecipeParser.TryParse(
            new Uri("https://example.com/pancakes"), Html, providerKey: "learned:example.com", blueprint);

        result.Should().BeNull();
    }
}

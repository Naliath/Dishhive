using System.Net;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Tests.Mocks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// Tests for the recipe-scrapers sidecar fallback provider with a mocked sidecar HTTP API
/// </summary>
public class RecipeScrapersFallbackProviderTests
{
    private const string SidecarBaseUrl = "http://scraper:8000/";
    private static readonly Uri RecipeUrl = new("https://www.example-recipes.test/pasta-pesto");

    private const string ScrapeResponseJson = """
        {
          "title": "Pasta pesto",
          "description": "Quick weeknight pasta.",
          "ingredients": ["300 g pasta", "1 jar pesto", "50 g parmezaan"],
          "instructions": ["Kook de pasta.", "Roer de pesto erdoor.", "Werk af met parmezaan."],
          "yields": "4 servings",
          "image": "https://www.example-recipes.test/images/pasta.jpg",
          "prepTimeMinutes": 10,
          "cookTimeMinutes": 15,
          "totalTimeMinutes": 25,
          "category": "Hoofdgerecht",
          "keywords": ["pasta", "snel"],
          "canonicalUrl": "https://www.example-recipes.test/pasta-pesto",
          "host": "example-recipes.test",
          "scraperVersion": "15.11.0",
          "raw": "{\"title\": \"Pasta pesto\"}"
        }
        """;

    private static RecipeScrapersClient CreateClient(MockHttpMessageHandler handler, bool configured = true)
    {
        var httpClient = new HttpClient(handler);
        if (configured)
        {
            httpClient.BaseAddress = new Uri(SidecarBaseUrl);
        }

        return new RecipeScrapersClient(httpClient, NullLogger<RecipeScrapersClient>.Instance);
    }

    private static RecipeScrapersFallbackProvider CreateProvider(MockHttpMessageHandler handler, bool configured = true)
    {
        return new RecipeScrapersFallbackProvider(
            CreateClient(handler, configured),
            NullLogger<RecipeScrapersFallbackProvider>.Instance);
    }

    [Fact]
    public void CanHandle_NotConfigured_ReturnsFalse()
    {
        var provider = CreateProvider(new MockHttpMessageHandler(), configured: false);

        provider.CanHandle(RecipeUrl).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_ConfiguredHttpUrl_ReturnsTrue()
    {
        var provider = CreateProvider(new MockHttpMessageHandler());

        provider.CanHandle(RecipeUrl).Should().BeTrue();
    }

    [Fact]
    public async Task Extract_MapsSidecarResponseToImportedRecipe()
    {
        var handler = new MockHttpMessageHandler()
            .RespondWith(SidecarBaseUrl + "scrape", ScrapeResponseJson, "application/json");
        var provider = CreateProvider(handler);

        var recipe = await provider.ExtractAsync("<html>irrelevant</html>", RecipeUrl);

        recipe.Title.Should().Be("Pasta pesto");
        recipe.Description.Should().Be("Quick weeknight pasta.");
        recipe.IngredientLines.Should().Equal("300 g pasta", "1 jar pesto", "50 g parmezaan");
        recipe.Steps.Should().HaveCount(3);
        recipe.Steps[0].Should().Be("Kook de pasta.");
        recipe.Servings.Should().Be(4);
        recipe.ImageUrl.Should().Be("https://www.example-recipes.test/images/pasta.jpg");
        recipe.SourceUrl.Should().Be("https://www.example-recipes.test/pasta-pesto");
        recipe.PrepTimeMinutes.Should().Be(10);
        recipe.CookTimeMinutes.Should().Be(15);
        recipe.TotalTimeMinutes.Should().Be(25);
        recipe.Category.Should().Be("Hoofdgerecht");
        recipe.Keywords.Should().Be("pasta, snel");
        recipe.RawData.Should().Contain("Pasta pesto");
    }

    [Fact]
    public async Task Extract_SidecarFindsNoRecipe_ThrowsExtractionFailed()
    {
        // 422 is the sidecar's "no recipe in this page" answer
        var handler = new MockHttpMessageHandler()
            .FailWith(SidecarBaseUrl + "scrape", HttpStatusCode.UnprocessableEntity);
        var provider = CreateProvider(handler);

        var act = () => provider.ExtractAsync("<html>no recipe</html>", RecipeUrl);

        await act.Should().ThrowAsync<RecipeExtractionFailedException>()
            .WithMessage("*No recipe data found*");
    }

    [Fact]
    public async Task Extract_SidecarDown_ThrowsExtractionFailedWithServiceHint()
    {
        var handler = new MockHttpMessageHandler()
            .FailWith(SidecarBaseUrl + "scrape", HttpStatusCode.ServiceUnavailable);
        var provider = CreateProvider(handler);

        var act = () => provider.ExtractAsync("<html></html>", RecipeUrl);

        await act.Should().ThrowAsync<RecipeExtractionFailedException>()
            .WithMessage("*scraper service*");
    }
}

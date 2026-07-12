using Dishhive.Api.Services.WebSearch;
using Dishhive.Api.Tests.Mocks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dishhive.Api.Tests.Services;

/// <summary>SearXNG JSON response mapping and the site: query restriction.</summary>
public class SearxngWebSearchClientTests
{
    private const string BaseUrl = "http://searxng:8080/";

    private const string JsonResponse = """
        {
          "results": [
            {"url": "https://dagelijksekost.vrt.be/a", "title": "Vegetarische lasagne", "content": "30 min"},
            {"url": "https://dagelijksekost.vrt.be/b", "title": "Groentecurry", "content": "quick"},
            {"url": "", "title": "no url — dropped"}
          ]
        }
        """;

    private static SearxngWebSearchClient CreateClient(MockHttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        return new SearxngWebSearchClient(
            http, Substitute.For<ISitemapRecipeSearch>(), NullLogger<SearxngWebSearchClient>.Instance);
    }

    [Fact]
    public async Task Search_MapsResultsAndDropsIncompleteHits()
    {
        var handler = new MockHttpMessageHandler().RespondWith(BaseUrl + "search", JsonResponse, "application/json");

        var results = await CreateClient(handler).SearchAsync("vegetarian", site: null, count: 5);

        results.Should().HaveCount(2);
        results[0].Title.Should().Be("Vegetarische lasagne");
        results[0].Url.Should().Be("https://dagelijksekost.vrt.be/a");
        results[0].Snippet.Should().Be("30 min");
    }

    [Fact]
    public async Task Search_WithSite_AddsSiteOperatorToQuery()
    {
        var handler = new MockHttpMessageHandler().RespondWith(BaseUrl + "search", JsonResponse, "application/json");

        await CreateClient(handler).SearchAsync("vegetarian pasta", site: "dagelijksekost.vrt.be", count: 5);

        var query = Uri.UnescapeDataString(handler.Requests.Single().Query);
        query.Should().Contain("site:dagelijksekost.vrt.be vegetarian pasta");
    }

    [Fact]
    public async Task Search_RespectsCount()
    {
        var handler = new MockHttpMessageHandler().RespondWith(BaseUrl + "search", JsonResponse, "application/json");

        var results = await CreateClient(handler).SearchAsync("x", site: null, count: 1);

        results.Should().ContainSingle();
    }

    [Fact]
    public async Task Search_NotConfigured_ReturnsEmpty()
    {
        var client = new SearxngWebSearchClient(
            new HttpClient(), Substitute.For<ISitemapRecipeSearch>(), NullLogger<SearxngWebSearchClient>.Instance);

        var results = await client.SearchAsync("x", site: null, count: 5);

        results.Should().BeEmpty();
        client.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task Search_BackendError_ReturnsEmptyInsteadOfThrowing()
    {
        var handler = new MockHttpMessageHandler().FailWith(BaseUrl + "search", System.Net.HttpStatusCode.BadGateway);

        var results = await CreateClient(handler).SearchAsync("x", site: null, count: 5);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_EmptySiteResults_UsesSitemapFallback()
    {
        var handler = new MockHttpMessageHandler().RespondWith(BaseUrl + "search", "{\"results\":[]}", "application/json");
        var sitemap = Substitute.For<ISitemapRecipeSearch>();
        sitemap.SearchAsync("vegetarisch", "example.com", 5, Arg.Any<CancellationToken>())
            .Returns([new WebSearchResult("Vegetarische curry", "https://example.com/vegetarische-curry", null)]);
        var client = new SearxngWebSearchClient(
            new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) },
            sitemap,
            NullLogger<SearxngWebSearchClient>.Instance);

        var results = await client.SearchAsync("vegetarisch", "example.com", 5);

        results.Should().ContainSingle().Which.Title.Should().Be("Vegetarische curry");
    }

    [Fact]
    public async Task Search_FiltersCollectionPagesAndSupplementsWithSitemapRecipes()
    {
        const string response = """
            {"results":[
              {"url":"https://example.com/category/desserts/","title":"106+ dessert recepten"},
              {"url":"https://example.com/chocolate-mousse/","title":"Chocolate mousse"}
            ]}
            """;
        var handler = new MockHttpMessageHandler().RespondWith(BaseUrl + "search", response, "application/json");
        var sitemap = Substitute.For<ISitemapRecipeSearch>();
        sitemap.SearchAsync("dessert", "example.com", 2, Arg.Any<CancellationToken>())
            .Returns([new WebSearchResult("Strawberry cheesecake", "https://example.com/strawberry-cheesecake/", null)]);
        var client = new SearxngWebSearchClient(
            new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) }, sitemap,
            NullLogger<SearxngWebSearchClient>.Instance);

        var results = await client.SearchAsync("dessert", "example.com", 2);

        results.Select(result => result.Url).Should().BeEquivalentTo(
            "https://example.com/chocolate-mousse/",
            "https://example.com/strawberry-cheesecake/");
    }
}

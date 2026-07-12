using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.WebSearch;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dishhive.Api.Tests.Services;

public class SitemapRecipeSearchTests
{
    [Fact]
    public async Task Search_IndexesRecipeSitemapAndRanksUnicodeTermsWithoutLanguageMappings()
    {
        const string host = "recipes.example";
        const string root = $"https://{host}/sitemap.xml";
        const string recipes = $"https://{host}/recipe-sitemap.xml";
        var fetcher = Substitute.For<ISafeHttpFetcher>();
        fetcher.GetAsync($"https://{host}/robots.txt", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Resource($"Sitemap: {root}"));
        fetcher.GetAsync(root, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Resource(SitemapIndex(recipes)));
        fetcher.GetAsync(recipes, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Resource(UrlSet(
                $"https://{host}/gerechten/vegetarische-curry",
                $"https://{host}/gerechten/kip-met-rijst")));
        var search = new SitemapRecipeSearch(fetcher, NullLogger<SitemapRecipeSearch>.Instance);

        var vegetarian = await search.SearchAsync("vegetarische", host, 5);
        var chicken = await search.SearchAsync("kip", host, 5);

        vegetarian.Should().ContainSingle().Which.Url.Should().Contain("vegetarische-curry");
        chicken.Should().ContainSingle().Which.Url.Should().Contain("kip-met-rijst");
        await fetcher.Received(1).GetAsync(recipes, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_IgnoresNestedImageLocations()
    {
        const string host = "recipes.example";
        const string root = $"https://{host}/sitemap.xml";
        var fetcher = Substitute.For<ISafeHttpFetcher>();
        fetcher.GetAsync($"https://{host}/robots.txt", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Resource($"Sitemap: {root}"));
        fetcher.GetAsync(root, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Resource(
            $"""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:image="http://www.google.com/schemas/sitemap-image/1.1"><url><loc>https://{host}/chocolate-cake/</loc><image:image><image:loc>https://{host}/wp-content/cake.jpg</image:loc></image:image></url></urlset>"""));

        var results = await new SitemapRecipeSearch(fetcher, NullLogger<SitemapRecipeSearch>.Instance)
            .SearchAsync("cake", host, 5);

        results.Should().ContainSingle().Which.Url.Should().Be($"https://{host}/chocolate-cake/");
    }

    [Fact]
    public async Task Search_DoesNotGuessWhetherLanguageSpecificTitlesAreCollections()
    {
        const string host = "recipes.example";
        const string root = $"https://{host}/sitemap.xml";
        var fetcher = Substitute.For<ISafeHttpFetcher>();
        fetcher.GetAsync($"https://{host}/robots.txt", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Resource($"Sitemap: {root}"));
        fetcher.GetAsync(root, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Resource(UrlSet(
            $"https://{host}/10-makkelijke-kerstdesserts/",
            $"https://{host}/dessert-inspiratie/",
            $"https://{host}/appel-karamel-monchou-dessert/",
            $"https://{host}/bounty-yoghurt-toetje/")));

        var results = await new SitemapRecipeSearch(fetcher, NullLogger<SitemapRecipeSearch>.Instance)
            .SearchAsync("desserts", host, 5);

        results.Should().ContainSingle().Which.Url.Should().Be(
            $"https://{host}/10-makkelijke-kerstdesserts/");
    }

    private static FetchedHttpResource Resource(string text) => new(
        new Uri("https://recipes.example/resource"), "application/xml", "utf-8", System.Text.Encoding.UTF8.GetBytes(text));

    private static string SitemapIndex(params string[] urls) =>
        $"<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">{string.Join("", urls.Select(url => $"<sitemap><loc>{url}</loc></sitemap>"))}</sitemapindex>";

    private static string UrlSet(params string[] urls) =>
        $"<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">{string.Join("", urls.Select(url => $"<url><loc>{url}</loc></url>"))}</urlset>";
}

using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Suggestions;
using Dishhive.Api.Services.WebSearch;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// The external-recipe tools memoize per suggestion request: a model sometimes
/// re-issues an identical search_recipes/get_recipe call, and re-fetching would waste
/// the shared agent time budget on a repeat (observed in production logs — a duplicate
/// get_recipe call on the same URL contributed to an agentic request timing out).
/// </summary>
public class ExternalRecipeToolsTests
{
    private static ExternalRecipeTools CreateTools(IWebSearchClient webSearch, IRecipeImportService importService)
        => new(webSearch, importService, maxResults: 5, defaultSite: null, NullLogger.Instance);

    [Fact]
    public async Task GetRecipe_SameUrlTwice_OnlyFetchesOnce()
    {
        var importService = Substitute.For<IRecipeImportService>();
        importService.PreviewAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RecipePreview(true, new ImportedRecipe { Title = "Dish" }, null, null)));
        var tools = CreateTools(Substitute.For<IWebSearchClient>(), importService);

        const string url = "https://dagelijksekost.vrt.be/gerechten/vegetarische-wok";
        var first = await tools.GetRecipeAsync(url);
        var second = await tools.GetRecipeAsync(url);

        first.Title.Should().Be("Dish");
        second.Title.Should().Be("Dish");
        await importService.Received(1).PreviewAsync(url, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRecipe_DifferentUrls_FetchesEach()
    {
        var importService = Substitute.For<IRecipeImportService>();
        importService.PreviewAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RecipePreview(true, new ImportedRecipe { Title = "Dish" }, null, null)));
        var tools = CreateTools(Substitute.For<IWebSearchClient>(), importService);

        await tools.GetRecipeAsync("https://example.com/a");
        await tools.GetRecipeAsync("https://example.com/b");

        await importService.Received(1).PreviewAsync("https://example.com/a", Arg.Any<CancellationToken>());
        await importService.Received(1).PreviewAsync("https://example.com/b", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchRecipes_SameQueryAndSiteTwice_OnlySearchesOnce()
    {
        var webSearch = Substitute.For<IWebSearchClient>();
        webSearch.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WebSearchResult>>(
                [new WebSearchResult("Title", "https://example.com/x", "snippet")]));
        var tools = CreateTools(webSearch, Substitute.For<IRecipeImportService>());

        var first = await tools.SearchRecipesAsync("vegetarian pasta", "example.com");
        var second = await tools.SearchRecipesAsync("vegetarian pasta", "example.com");

        first.Should().ContainSingle();
        second.Should().ContainSingle();
        await webSearch.Received(1).SearchAsync(
            "vegetarian pasta", "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchRecipes_SameQueryDifferentSite_SearchesEach()
    {
        var webSearch = Substitute.For<IWebSearchClient>();
        webSearch.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WebSearchResult>>([]));
        var tools = CreateTools(webSearch, Substitute.For<IRecipeImportService>());

        await tools.SearchRecipesAsync("pasta", "a.example");
        await tools.SearchRecipesAsync("pasta", "b.example");

        await webSearch.Received(1).SearchAsync("pasta", "a.example", Arg.Any<int>(), Arg.Any<CancellationToken>());
        await webSearch.Received(1).SearchAsync("pasta", "b.example", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}

using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Suggestions;
using Dishhive.Api.Services.WebSearch;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dishhive.Api.Tests.Services;

public class ExternalRecipeToolsTests
{
    private static ExternalRecipeTools CreateTools(
        IWebSearchClient webSearch,
        IRecipeImportService importService,
        IReadOnlyCollection<string>? allowedHosts = null)
        => new(webSearch, importService, 5,
            allowedHosts?.Count == 1 ? allowedHosts.Single() : null,
            allowedHosts,
            "test",
            NullLogger.Instance);

    private static IWebSearchClient SearchReturning(params WebSearchResult[] results)
    {
        var search = Substitute.For<IWebSearchClient>();
        search.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WebSearchResult>>(results));
        return search;
    }

    [Fact]
    public async Task SearchRecipes_AssignsOpaqueCandidateId()
    {
        var tools = CreateTools(
            SearchReturning(new WebSearchResult("Dish", "https://example.com/dish", "snippet")),
            Substitute.For<IRecipeImportService>(),
            ["example.com"]);

        var result = await tools.SearchRecipesAsync("dish", "example.com");

        var hit = result.Should().ContainSingle().Subject;
        hit.CandidateId.Should().Be("c1");
        hit.Title.Should().Be("Dish");
    }

    [Fact]
    public async Task GetRecipe_SameCandidateTwice_OnlyFetchesOnceAndBecomesResolvable()
    {
        const string url = "https://dagelijksekost.vrt.be/gerechten/vegetarische-wok";
        var importService = Substitute.For<IRecipeImportService>();
        importService.PreviewAsync(url, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RecipePreview(
                true,
                new ImportedRecipe { Title = "Dish", SourceUrl = url, IngredientLines = ["1 onion"] },
                null,
                null)));
        var tools = CreateTools(
            SearchReturning(new WebSearchResult("Dish", url, null)),
            importService,
            ["dagelijksekost.vrt.be"]);
        var candidateId = (await tools.SearchRecipesAsync("dish", null)).Single().CandidateId;

        var first = await tools.GetRecipeAsync(candidateId);
        var second = await tools.GetRecipeAsync(candidateId);

        first.CandidateId.Should().Be(candidateId);
        first.Title.Should().Be("Dish");
        second.Should().BeSameAs(first);
        tools.TryResolveCandidate(candidateId, out var candidate).Should().BeTrue();
        candidate!.SourceUrl.Should().Be(url);
        candidate.Ingredients.Should().ContainSingle("1 onion");
        await importService.Received(1).PreviewAsync(url, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRecipe_UnknownCandidate_IsRejectedWithoutFetch()
    {
        var importService = Substitute.For<IRecipeImportService>();
        var tools = CreateTools(Substitute.For<IWebSearchClient>(), importService, ["allowed.example"]);

        var result = await tools.GetRecipeAsync("c999");

        result.Error.Should().Contain("Unknown candidateId");
        await importService.DidNotReceive().PreviewAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchRecipes_ResultOutsideReferencedHosts_IsDiscarded()
    {
        var tools = CreateTools(
            SearchReturning(new WebSearchResult("Wrong host", "https://other.example/dish", null)),
            Substitute.For<IRecipeImportService>(),
            ["allowed.example"]);

        var result = await tools.SearchRecipesAsync("dish", "allowed.example");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchRecipes_SameQueryAndSiteTwice_OnlySearchesOnce()
    {
        var webSearch = SearchReturning(new WebSearchResult("Title", "https://example.com/x", "snippet"));
        var tools = CreateTools(webSearch, Substitute.For<IRecipeImportService>(), ["example.com"]);

        var first = await tools.SearchRecipesAsync("vegetarian pasta", "example.com");
        var second = await tools.SearchRecipesAsync("vegetarian pasta", "example.com");

        first.Should().ContainSingle();
        second.Should().ContainSingle();
        second.Single().CandidateId.Should().Be(first.Single().CandidateId);
        await webSearch.Received(1).SearchAsync(
            "vegetarian pasta", "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}

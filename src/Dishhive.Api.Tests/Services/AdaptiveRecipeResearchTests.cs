using Dishhive.Api.Models;
using Dishhive.Api.Services.Facts;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Suggestions;
using Dishhive.Api.Services.WebSearch;
using Dishhive.Api.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dishhive.Api.Tests.Services;

public sealed class AdaptiveRecipeResearchTests
{
    [Fact]
    public async Task AFactEligibleWrongFoodCannotFinishResearchOrAcquireSemanticProvenance()
    {
        var search = Substitute.For<IWebSearchClient>();
        search.SearchAsync("pancakes", "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Hits("meatballs", 1));
        search.SearchAsync("crepes", "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Hits("crepes", 1));
        var matcher = Substitute.For<IRecipeDishMatcher>();
        matcher.MatchAsync("pannekoeken", Arg.Any<IReadOnlyList<DishMatchCandidate>>(), "research-test", Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<string>?>(call.ArgAt<IReadOnlyList<DishMatchCandidate>>(1)
                .Where(candidate => candidate.Title.Contains("crepes")).Select(candidate => candidate.Id).ToList()));
        var tools = Tools(search, matcher: matcher);
        var results = await tools.ResearchRecipesAsync([new("food", "pancakes", "example.com", 1,
            ExcludedClasses: [IngredientClass.Milk], AlternativeQueries: ["crepes"], FoodRequest: "pannekoeken")]);
        Assert.Contains("crepes", Assert.Single(results).Title);
        Assert.True(tools.TryResolveCandidate("c1", out var wrongFood));
        Assert.Empty(wrongFood!.ResearchConstraintIds);
        Assert.True(tools.TryResolveCandidate("c2", out var matchingFood));
        Assert.Equal(new[] { "food" }, matchingFood!.ResearchConstraintIds);
    }

    [Fact]
    public async Task SlowClassificationsKeepABoundedParallelWaveWhenOnlyOneCandidateIsMissing()
    {
        var search = Substitute.For<IWebSearchClient>();
        search.SearchAsync(Arg.Any<string>(), "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Hits("candidate", 8));
        var importer = Substitute.For<IRecipeImportService>();
        importer.PreviewAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(new RecipePreview(true,
            new ImportedRecipe { Title = "Candidate", SourceUrl = call.ArgAt<string>(0), IngredientLines = ["ingredient"], Steps = ["Cook"] }, null, null)));
        var facts = Substitute.For<IRecipeFactsExtractor>();
        facts.IsAvailable.Returns(true);
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<IngredientClass>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        facts.ExtractAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<string>()).Returns(_ =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call <= 4) return Task.FromResult<IReadOnlyList<IngredientClass>?>(call == 4 ? [IngredientClass.Milk] : []);
            if (call == 8) allStarted.TrySetResult();
            return release.Task;
        });
        var tools = new ExternalRecipeTools(search, importer, 8, null, ["example.com"], "parallel",
            NullLogger.Instance, facts, prefetchRemaining: true);
        var researching = tools.ResearchRecipesAsync([new("four", "food", "example.com", 4, ExcludedClasses: [IngredientClass.Milk])]);
        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult([]);
        var results = await researching;
        Assert.Equal(8, calls);
        Assert.InRange(results.Count, 4, 8);
        Assert.All(results, result => Assert.DoesNotContain(IngredientClass.Milk, result.ContainsClasses));
    }

    [Fact]
    public async Task RefinementReceivesBoundedAssessedSuccessesAndRejectionsForFoodDiversity()
    {
        var search = Substitute.For<IWebSearchClient>();
        search.SearchAsync("first", "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<WebSearchResult>>([
                new("One", "https://example.com/dish/plant-first", null),
                new("Two", "https://example.com/dish/milk-two", null),
                new("Three", "https://example.com/dish/milk-three", null),
                new("Four", "https://example.com/dish/milk-four", null)]));
        search.SearchAsync("different food", "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Hits("plant-new", 4));
        var refiner = Substitute.For<IRecipeResearchQueryRefiner>();
        refiner.RefineAsync(Arg.Any<RecipeResearchFeedback>(), "research-test", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["different food"]));
        var results = await Tools(search, refiner: refiner).ResearchRecipesAsync([new("four", "first", "example.com", 4,
            ExcludedClasses: [IngredientClass.Milk])]);
        Assert.Equal(4, results.Count);
        await refiner.Received(1).RefineAsync(Arg.Is<RecipeResearchFeedback>(feedback => feedback.Examples != null
            && feedback.Examples.Count <= 6 && feedback.Examples.Any(example => example.Eligible)
            && feedback.Examples.Any(example => !example.Eligible && example.ContainsClasses.Contains(IngredientClass.Milk))),
            "research-test", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResearchStopsToPreserveTheConfiguredGenerationAllowance()
    {
        var search = Substitute.For<IWebSearchClient>();
        search.SearchAsync(Arg.Any<string>(), "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Hits("plant", 12));
        using var operation = new AiOperationBudget(60, 6, CancellationToken.None);
        var tools = Tools(search, countAttempts: true);
        var results = await tools.ResearchRecipesAsync([new("seven", "food", "example.com", 7,
            ExcludedClasses: [IngredientClass.Milk])]);
        Assert.Equal(4, results.Count);
        Assert.Equal(4, operation.Attempts);
        Assert.Equal(2, operation.RemainingAttempts);
    }

    [Fact]
    public async Task ExhaustedRefinementCanUseTheRemainingInspectionBudgetWithOneFurtherCorrection()
    {
        var search = Substitute.For<IWebSearchClient>();
        search.SearchAsync(Arg.Any<string>(), "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Hits(call.ArgAt<string>(0) == "third" ? "plant" : "milk-" + call.ArgAt<string>(0), 5));
        var refiner = Substitute.For<IRecipeResearchQueryRefiner>();
        refiner.RefineAsync(Arg.Any<RecipeResearchFeedback>(), "research-test", Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<string>>(
                call.ArgAt<RecipeResearchFeedback>(0).SearchedQueries.Contains("second") ? ["third"] : ["second"]));
        var tools = Tools(search, refiner: refiner);
        var results = await tools.ResearchRecipesAsync([new("two", "first", "example.com", 2,
            ExcludedClasses: [IngredientClass.Milk])]);
        Assert.Equal(2, results.Count);
        Assert.InRange(tools.GetMetrics().ResolutionCount, 1, 28);
        await refiner.Received(2).RefineAsync(Arg.Any<RecipeResearchFeedback>(), "research-test", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectedIngredientsRefineRetrievalBeforeSpendingTheRemainingBudget()
    {
        var search = Substitute.For<IWebSearchClient>();
        search.SearchAsync("first", "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Hits("milk", 5));
        search.SearchAsync("refined", "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Hits("plant", 5));
        var refiner = Substitute.For<IRecipeResearchQueryRefiner>();
        refiner.RefineAsync(Arg.Any<RecipeResearchFeedback>(), "research-test", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["refined"]));
        var tools = Tools(search, refiner: refiner);
        var result = await tools.ResearchRecipesAsync([new("four", "first", "example.com", 4,
            ExcludedClasses: [IngredientClass.Milk], AlternativeQueries: ["ineffective"])]);
        Assert.Equal(4, result.Count);
        Assert.Equal(8, tools.GetMetrics().ResolutionCount);
        await refiner.Received(1).RefineAsync(Arg.Is<RecipeResearchFeedback>(feedback =>
            feedback.ExcludedClasses.Contains(IngredientClass.Milk) && feedback.RejectedClasses.Contains(IngredientClass.Milk)
            && feedback.EligibleCount == 0 && feedback.TargetCount == 4), "research-test", Arg.Any<CancellationToken>());
        await search.DidNotReceive().SearchAsync("ineffective", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefinementDoesNotLoseUninspectedEligibleHitsFromTheOriginalSearch()
    {
        var search = Substitute.For<IWebSearchClient>();
        search.SearchAsync("first", "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<WebSearchResult>>(Enumerable.Range(0, 6).Select(index => new WebSearchResult(
                "Candidate", $"https://example.com/dish/{(index < 4 ? "milk" : "plant")}{index}", null)).ToList()));
        search.SearchAsync("correction", "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Hits("milk-correction", 1));
        var refiner = Substitute.For<IRecipeResearchQueryRefiner>();
        refiner.RefineAsync(Arg.Any<RecipeResearchFeedback>(), "research-test", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["correction"]));
        var results = await Tools(search, refiner: refiner).ResearchRecipesAsync([new("two", "first", "example.com", 2,
            ExcludedClasses: [IngredientClass.Milk])]);
        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Empty(result.ContainsClasses));
    }

    [Fact]
    public async Task EmptyOptionalRefinementPreservesTheOriginalFallbacks()
    {
        var search = Substitute.For<IWebSearchClient>();
        search.SearchAsync("first", "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Hits("milk", 5));
        search.SearchAsync("original", "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Hits("plant", 5));
        var tools = Tools(search, refiner: new NoOpRecipeResearchQueryRefiner());
        var results = await tools.ResearchRecipesAsync([new("one", "first", "example.com", 1,
            ExcludedClasses: [IngredientClass.Milk], AlternativeQueries: ["original"])]);
        Assert.Single(results);
    }

    [Fact]
    public async Task RepeatedSearchUrlsDoNotUseTheInspectionBudgetOrHideALaterUsefulQuery()
    {
        var search = Substitute.For<IWebSearchClient>();
        foreach (var query in new[] { "first", "same", "same-again" })
            search.SearchAsync(query, "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Hits("milk", 5));
        search.SearchAsync("different", "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Hits("plant", 5));
        var tools = Tools(search);
        var results = await tools.ResearchRecipesAsync([new("four", "first", "example.com", 4,
            ExcludedClasses: [IngredientClass.Milk], AlternativeQueries: ["same", "same-again", "different"])]);
        Assert.Equal(4, results.Count);
        Assert.Equal(9, tools.GetMetrics().ResolutionCount);
        Assert.Equal(4, tools.GetMetrics().SearchCount);
    }

    [Fact]
    public async Task DifficultEarlierRequirementsCannotConsumeEveryCandidateBeforeALaterClauseIsConsidered()
    {
        var search = Substitute.For<IWebSearchClient>();
        search.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Hits(call.ArgAt<string>(0) == "last" ? "plant" : "milk-" + call.ArgAt<string>(0), 5));
        var tools = Tools(search);
        var results = await tools.ResearchRecipesAsync([
            new("a", "first", "example.com", 7, ExcludedClasses: [IngredientClass.Milk], AlternativeQueries: ["a2", "a3", "a4"]),
            new("b", "second", "example.com", 7, ExcludedClasses: [IngredientClass.Milk], AlternativeQueries: ["b2", "b3", "b4"]),
            new("c", "last", "example.com", 1, ExcludedClasses: [IngredientClass.Milk])]);
        Assert.Contains(results, result => result.ResearchConstraintIds.Contains("c"));
        Assert.InRange(tools.GetMetrics().ResolutionCount, 1, 28);
    }

    [Fact]
    public async Task SparseEligibleResults_UseFallbackQueryUntilTheRequestedDistinctCountIsAvailable()
    {
        var search = Substitute.For<IWebSearchClient>();
        search.SearchAsync("first", "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Hits("milk", 5));
        search.SearchAsync("fallback", "example.com", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Hits("plant", 5));
        var tools = Tools(search);
        var result = await tools.ResearchRecipesAsync([new("four", "first", "example.com", 4,
            ExcludedClasses: [IngredientClass.Milk], AlternativeQueries: ["fallback"])]);
        Assert.Equal(4, result.Count);
        Assert.All(result, item => { Assert.True(item.FactsAssessed); Assert.DoesNotContain(IngredientClass.Milk, item.ContainsClasses); });
        Assert.All(result, item => Assert.Equal(new[] { "four" }, item.ResearchConstraintIds));
        Assert.Equal(9, tools.GetMetrics().ResolutionCount);
    }

    [Fact]
    public async Task EnoughEligibleRecipes_StopBeforeOtherQueries_WithoutInferringFromTheirTitles()
    {
        var search = Substitute.For<IWebSearchClient>();
        search.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Hits("plant", 5));
        var tools = Tools(search);
        var result = await tools.ResearchRecipesAsync([new("two", "first", "example.com", 2,
            ExcludedClasses: [IngredientClass.Milk], AlternativeQueries: ["unneeded"])]);
        Assert.True(result.Count >= 2);
        await search.DidNotReceive().SearchAsync("unneeded", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        Assert.All(result, item => Assert.True(item.FactsAssessed));
    }

    [Fact]
    public async Task SourceHostMustMatchTheResearchedClause_EvenWhenOtherHostsAreReferenced()
    {
        var search = Substitute.For<IWebSearchClient>();
        search.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<WebSearchResult>>([new("Other source", "https://other.example/plant", null)]));
        var result = await Tools(search, ["example.com", "other.example"]).ResearchRecipesAsync([new("source", "first", "example.com")]);
        Assert.Empty(result);
    }

    [Fact]
    public async Task RepeatedUnusableResults_RemainBoundedInsteadOfCrawlingWithoutLimit()
    {
        var search = Substitute.For<IWebSearchClient>();
        search.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Hits("milk-" + call.ArgAt<string>(0), 5));
        var tools = Tools(search);
        var requests = Enumerable.Range(0, 4).Select(index => new ExternalRecipeTools.RecipeResearchRequest(
            index.ToString(), "query" + index, "example.com", 7, ExcludedClasses: [IngredientClass.Milk],
            AlternativeQueries: ["alternative" + index, "last" + index])).ToList();
        Assert.Empty(await tools.ResearchRecipesAsync(requests));
        Assert.InRange(tools.GetMetrics().ResolutionCount, 1, 28);
    }

    private static Task<IReadOnlyList<WebSearchResult>> Hits(string marker, int count) =>
        Task.FromResult<IReadOnlyList<WebSearchResult>>(Enumerable.Range(0, count)
            .Select(index => new WebSearchResult("A recipe title is not dietary evidence", $"https://example.com/dish/{marker}{index}", null)).ToList());

    private static ExternalRecipeTools Tools(IWebSearchClient search, string[]? hosts = null, IRecipeResearchQueryRefiner? refiner = null,
        bool countAttempts = false, IRecipeDishMatcher? matcher = null)
    {
        var importer = Substitute.For<IRecipeImportService>();
        importer.PreviewAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(new RecipePreview(true,
            new ImportedRecipe { Title = call.ArgAt<string>(0), SourceUrl = call.ArgAt<string>(0), IngredientLines = ["Ingredient"], Steps = ["Cook"] }, null, null)));
        var facts = Substitute.For<IRecipeFactsExtractor>();
        facts.IsAvailable.Returns(true);
        facts.ExtractAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>(), Arg.Any<string>())
            .Returns(call => {
                if (countAttempts) AiOperationBudget.Current!.TakeAttempt();
                return Task.FromResult<IReadOnlyList<IngredientClass>?>(call.ArgAt<string>(0).Contains("milk") ? [IngredientClass.Milk] : []);
            });
        return new(search, importer, 5, null, hosts ?? ["example.com"], "research-test", NullLogger.Instance, facts, refiner, dishMatcher: matcher);
    }
}

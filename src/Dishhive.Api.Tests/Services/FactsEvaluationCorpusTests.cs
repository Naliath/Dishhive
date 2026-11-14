using Dishhive.Api.Models;
using Dishhive.Evaluations;

namespace Dishhive.Api.Tests.Services;

public sealed class FactsEvaluationCorpusTests
{
    private static FactsEvaluationCorpus Corpus() => FactsEvaluationCorpus.Load(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "RecipeFacts", "scenarios.json"));

    [Fact]
    public void CorpusHasIndependentExpectationsAndExplicitUncertaintyBoundaries()
    {
        var corpus = Corpus();
        Assert.True(corpus.Cases.Count >= 20);
        Assert.All(corpus.Cases, item => Assert.False(string.IsNullOrWhiteSpace(item.Basis)));
        Assert.Contains(corpus.Cases, item => item.Exact && item.RequiredClasses.Count == 0);
        Assert.Contains(corpus.Cases, item => !item.Exact && item.SourceUrl != null);
    }

    [Fact]
    public void KnownMissingGambaClass_CannotPassTheRecallCase()
    {
        var item = Corpus().Cases.Single(item => item.Id == "mixed-gamba-stock");
        var assessment = item.Assess([IngredientClass.Gluten, IngredientClass.Fish, IngredientClass.Milk, IngredientClass.Celery]);
        Assert.False(assessment.Passed);
        Assert.Equal(new[] { "Crustaceans" }, assessment.Missing);
    }

    [Fact]
    public void PlantDrinkFalsePositiveAndMissingCompletion_CannotPassAsKnownEmptyFacts()
    {
        var item = Corpus().Cases.Single(item => item.Id == "coconut-milk");
        Assert.True(item.Assess([]).Passed);
        Assert.False(item.Assess(null).Passed);
        Assert.False(item.Assess([IngredientClass.Milk]).Passed);
    }
}

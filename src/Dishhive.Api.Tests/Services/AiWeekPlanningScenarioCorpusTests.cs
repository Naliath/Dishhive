using System.Text.Json;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

public class AiWeekPlanningScenarioCorpusTests
{
    [Fact]
    public void ScenarioCorpus_HasUniqueRunnableCasesWithHardExpectations()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "AiWeekPlanning", "scenarios.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var scenarios = document.RootElement.EnumerateArray().ToList();

        scenarios.Should().NotBeEmpty();
        scenarios.Select(item => item.GetProperty("id").GetString())
            .Should().OnlyHaveUniqueItems().And.NotContainNulls();
        foreach (var scenario in scenarios)
        {
            scenario.GetProperty("prompt").GetString().Should().NotBeNullOrWhiteSpace();
            scenario.GetProperty("iterations").GetInt32().Should().BeGreaterThan(0);
            scenario.GetProperty("performanceBudgetMs").GetInt32().Should().BeGreaterThan(0);
            scenario.GetProperty("expectations").GetProperty("uniqueDishes").GetBoolean().Should().BeTrue();
            scenario.GetProperty("expectations").GetProperty("rejectCollectionPages").GetBoolean().Should().BeTrue();
            if (scenario.TryGetProperty("status", out var status))
            {
                status.GetString().Should().Be("known-limitation");
                scenario.GetProperty("limitation").GetString().Should().NotBeNullOrWhiteSpace();
            }
            foreach (var source in scenario.GetProperty("expectations").GetProperty("sources").EnumerateArray())
            {
                source.GetProperty("minDistinctRecipes").GetInt32().Should().BeGreaterThan(0);
                source.GetProperty("maxDistinctRecipes").GetInt32()
                    .Should().BeGreaterThanOrEqualTo(source.GetProperty("minDistinctRecipes").GetInt32());
                source.GetProperty("requiredClasses").ValueKind.Should().Be(JsonValueKind.Array);
                source.GetProperty("excludedClasses").ValueKind.Should().Be(JsonValueKind.Array);
            }
        }
    }
}

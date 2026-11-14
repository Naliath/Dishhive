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
            scenario.GetProperty("expectations").GetProperty("allowedStatuses")
                .EnumerateArray().Should().NotBeEmpty();
            scenario.GetProperty("expectations").GetProperty("allowedOrigins")
                .EnumerateArray().Should().NotBeEmpty();
            scenario.GetProperty("expectations").GetProperty("rejectAllergyWarnings")
                .GetBoolean().Should().BeTrue();
            if (scenario.GetProperty("expectations").TryGetProperty("minimumLocalRecipeLinks", out var minimumLinks))
            {
                minimumLinks.GetInt32().Should().BeGreaterThan(0);
            }
            if (scenario.GetProperty("expectations").TryGetProperty("requiredCollections", out var requiredCollections))
                requiredCollections.EnumerateArray().Should().NotBeEmpty().And.OnlyContain(item => !string.IsNullOrWhiteSpace(item.GetString()));
            if (scenario.TryGetProperty("selectedDayOffsets", out var selectedDayOffsets))
            {
                var offsets = selectedDayOffsets.EnumerateArray().Select(item => item.GetInt32()).ToList();
                offsets.Should().NotBeEmpty().And.OnlyHaveUniqueItems();
                offsets.Should().OnlyContain(offset => offset >= 0 && offset <= 6);
            }
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
            if (scenario.GetProperty("id").GetString() == "dutch-friday-pancakes-from-source")
            {
                scenario.GetProperty("expectations").GetProperty("sources")[0].GetProperty("foodIdentity")
                    .GetString().Should().Be("belgian-dutch-pancakes", "the user explicitly narrowed Friday's requested style");
                scenario.GetProperty("prompt").GetString().Should().Be("Op vrijdag pannekoeken van @[Dagelijkse Kost] als hoofd maaltijd");
                scenario.GetProperty("instructionClarification").GetString().Should().Contain("Belgian/Dutch");
                scenario.GetProperty("expectations").GetProperty("sources")[0].GetProperty("allowUnfilledWithExplanation").GetBoolean().Should().BeTrue();
                scenario.GetProperty("expectations").GetProperty("slots")[0].GetProperty("allowUnfilledWithExplanation").GetBoolean().Should().BeTrue();
            }
        }
    }
}

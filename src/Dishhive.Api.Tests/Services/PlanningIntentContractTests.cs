using Dishhive.Api.Models;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

public class PlanningIntentContractTests
{
    [Fact]
    public void Parse_RequiredClassForEveryone_DerivesDietPreferenceOverrideFromCanonicalFacts()
    {
        var monday = new DateOnly(2026, 6, 15);
        var request = new MealSuggestionRequest
        {
            WeekStart = monday,
            DaysToFill = [monday],
            Members = [new MemberProfile
            {
                Id = Guid.NewGuid(),
                Name = "Member",
                Diets = [new DietaryTagProfile
                {
                    Name = "Preference",
                    ExcludedClasses = [IngredientClass.Poultry]
                }]
            }]
        };
        const string json =
            """{"version":1,"allowRepeatedDishes":false,"constraints":[{"id":"food","dates":["2026-06-15"],"mealType":"dinner","course":"main","sourceHost":null,"count":1,"distinct":true,"searchQuery":"opaque","requiredClasses":["Poultry"],"excludedClasses":[],"allAttendees":true,"overrideDietPreferences":false}]}""";

        var intent = PlanningIntentContract.Parse(json, request);

        intent.Should().NotBeNull();
        intent!.Constraints.Should().ContainSingle().Which.OverrideDietPreferences.Should().BeTrue();
    }
}

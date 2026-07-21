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
        var json =
            $$"""{"version":{{PlanningIntent.CurrentVersion}},"allowRepeatedDishes":false,"constraints":[{"id":"food","dates":["2026-06-15"],"mealType":"dinner","course":"main","sourceHost":null,"count":1,"distinct":true,"searchQuery":"opaque","requiredClasses":["Poultry"],"excludedClasses":[],"allAttendees":true,"overrideDietPreferences":false}]}""";

        var intent = PlanningIntentContract.Parse(json, request);

        intent.Should().NotBeNull();
        intent!.Constraints.Should().ContainSingle().Which.OverrideDietPreferences.Should().BeTrue();
    }

    [Fact]
    public void Parse_ConstraintDateOutsideSelectedDates_IsRejected()
    {
        var monday = new DateOnly(2026, 6, 15);
        var request = new MealSuggestionRequest
        {
            WeekStart = monday,
            DaysToFill = [monday]
        };
        var json =
            $$"""{"version":{{PlanningIntent.CurrentVersion}},"allowRepeatedDishes":false,"constraints":[{"id":"wrong-day","dates":["2026-06-16"],"mealType":"dinner","course":"main","sourceHost":null,"count":1,"distinct":true,"searchQuery":null,"dishRequest":null,"requiredClasses":[],"excludedClasses":[],"allAttendees":true,"overrideDietPreferences":false}]}""";

        PlanningIntentContract.Parse(json, request).Should().BeNull();
    }
}

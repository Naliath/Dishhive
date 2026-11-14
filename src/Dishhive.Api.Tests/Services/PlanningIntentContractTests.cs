using Dishhive.Api.Models;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

public class PlanningIntentContractTests
{
    [Fact]
    public void ModelCannotSupplyItsOwnFoodProofOrForceAnExternalDishToALocalId()
    {
        var day = new DateOnly(2026, 11, 16);
        var request = new MealSuggestionRequest { DaysToFill = [day],
            KnownRecipes = [new() { Id = Guid.NewGuid(), Title = "Pancakes" }],
            SourceConstraints = [new() { Name = "Source", Host = "example.com" }] };
        var json = """{"version":10,"constraints":[{"id":"food","count":1,"dates":["2026-11-16"],"sourceHost":"example.com","searchQuery":"pancakes","dishRequest":"Pancakes","requiresDishMatch":false,"verifiedDishVersions":["invented"]}]}""";
        var constraint = Assert.Single(PlanningIntentContract.Parse(json, request)!.Constraints);
        Assert.True(constraint.RequiresDishMatch);
        Assert.Empty(constraint.VerifiedDishVersions);
        Assert.Empty(constraint.RequiredRecipeIds);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(7, false)]
    public void CountedDatesCannotBecomeABroaderSchedulingWindow(int dateCount, bool valid)
    {
        var monday = new DateOnly(2026, 11, 16);
        var selected = Enumerable.Range(0, 7).Select(monday.AddDays).ToArray();
        var request = new MealSuggestionRequest { DaysToFill = selected };
        var intent = new PlanningIntent { Constraints = [new()
        {
            Id = "four-meals", Count = 4, Dates = selected.Take(dateCount).ToArray()
        }] };

        var parsed = PlanningIntentContract.ParseWithErrors(PlanningIntentContract.FormatForModel(intent), request);

        Assert.Equal(valid, parsed.Intent != null);
        if (!valid)
            parsed.Errors.Should().ContainSingle().Which.Id.Should().Be("four-meals.dates");
        else
            parsed.Intent!.Constraints.Single().Count.Should().Be(4);
    }

    [Theory]
    [InlineData(3, 256, true)]
    [InlineData(4, 40, false)]
    [InlineData(1, 257, false)]
    public void SourceFallbackQueriesAreOpaqueAndBounded(int count, int length, bool valid)
    {
        var monday = new DateOnly(2026, 11, 16);
        var request = new MealSuggestionRequest { DaysToFill = [monday], SourceConstraints =
            [new SourceConstraint { Name = "Source", Host = "example.com" }] };
        var queries = Enumerable.Range(0, count).Select(index => index + new string('x', length - 1)).ToArray();
        var json = System.Text.Json.JsonSerializer.Serialize(new { version = PlanningIntent.CurrentVersion,
            constraints = new[] { new { id = "source", dates = new[] { "2026-11-16" }, sourceHost = "example.com",
                searchQuery = "specific food", searchQueries = queries } } });
        var parsed = PlanningIntentContract.ParseWithErrors(json, request);
        Assert.Equal(valid, parsed.Intent != null);
        if (valid) Assert.Equal(queries, parsed.Intent!.Constraints.Single().SearchQueries);
    }
    [Fact]
    public void Parse_RequiredCollectionUsesStableIdAndCannotBeDropped()
    {
        var monday = new DateOnly(2026, 11, 16);
        var request = new MealSuggestionRequest { DaysToFill = [monday], CollectionConstraints =
            [new CollectionConstraint { Id = "stable-id", Name = "Collection name", IsGlobal = true }] };
        var dropped = PlanningIntentContract.ParseWithErrors(
            """{"version":7,"constraints":[],"preferredCollectionIds":[]}""", request);
        dropped.Intent.Should().BeNull();
        dropped.Errors.Should().Contain(issue => issue.Id == "collections");

        var required = PlanningIntentContract.Parse(
            """{"version":7,"constraints":[{"id":"must","dates":["2026-11-16"],"requiredCollectionIds":["stable-id"]}]}""", request);
        required.Should().NotBeNull();
        required!.Constraints.Single().RequiredCollectionIds.Should().Equal("stable-id");

        PlanningIntentContract.Parse(
            """{"version":7,"constraints":[{"id":"must","requiredCollectionIds":["Collection name"]}]}""", request)
            .Should().BeNull();
    }

    [Fact]
    public void Parse_SoftCollectionPreferenceDoesNotBecomeAMandatoryCount()
    {
        var request = new MealSuggestionRequest { CollectionConstraints =
            [new CollectionConstraint { Id = "stable-id", Name = "Collection", IsGlobal = true }] };
        var intent = PlanningIntentContract.Parse(
            """{"version":7,"preferredCollectionIds":["stable-id"],"constraints":[]}""", request);

        intent.Should().NotBeNull();
        intent!.Constraints.Should().BeEmpty();
        intent.PreferredCollectionIds.Should().Equal("stable-id");
    }

    [Fact]
    public void Parse_DatedCollectionPreservesTheSpecificMealAndCourse()
    {
        var monday = new DateOnly(2026, 11, 16);
        var request = new MealSuggestionRequest { DaysToFill = [monday], CollectionConstraints =
            [new CollectionConstraint { Id = "stable-id", Name = "Desserts", Slots = [new(monday, MealType.Dinner, Course.Dessert)] }] };
        var intent = PlanningIntentContract.Parse("""{"version":7,"constraints":[]}""", request);

        intent.Should().NotBeNull();
        var constraint = intent!.Constraints.Should().ContainSingle().Subject;
        constraint.Course.Should().Be(Course.Dessert);
        constraint.RequiredCollectionIds.Should().Equal("stable-id");
    }

    [Fact]
    public void Parse_InvalidSourceQueryReturnsASpecificCorrectiveError()
    {
        var request = new MealSuggestionRequest { SourceConstraints =
            [new SourceConstraint { Name = "Source", Host = "example.test" }] };
        var parsed = PlanningIntentContract.ParseWithErrors(
            """{"version":7,"constraints":[{"id":"source","sourceHost":"example.test","searchQuery":null}]}""", request);

        parsed.Intent.Should().BeNull();
        parsed.Errors.Should().ContainSingle().Which.Id.Should().Be("source.searchQuery");
    }
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

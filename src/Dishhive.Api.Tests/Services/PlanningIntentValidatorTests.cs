using Dishhive.Api.Models;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

public class PlanningIntentValidatorTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void UnassessedMealsLeaveGapsOnlyForAttendeesWithCanonicalAllergies(int attendance, bool removed)
    {
        var allergic = Guid.NewGuid();
        var guest = Guid.NewGuid();
        var request = new MealSuggestionRequest { DaysToFill = [Monday], Members = [
            new MemberProfile { Id = allergic, Name = "Household member", Allergies = [new DietaryTagProfile
                { Name = "Milk", ExcludedClasses = [IngredientClass.Milk] }] },
            new MemberProfile { Id = guest, Name = "Guest" }] };
        var suggestion = new MealSuggestion { Date = Monday, DishName = "Unknown ingredients",
            AttendeeIds = attendance == 0 ? [guest] : attendance == 1 ? [allergic] : [] };
        var final = PlanningIntentValidator.ValidateFinal([suggestion], new(), request);
        Assert.Equal(removed ? 0 : 1, final.Suggestions.Count);
        Assert.Equal(removed, final.UnmetRequirements.Any(issue => issue.Id == "allergy-facts"));
        Assert.Equal(removed, final.UnmetRequirements.Any(issue => issue.Id.StartsWith("day-")));
    }

    private static readonly DateOnly Monday = new(2026, 6, 15);

    [Fact]
    public void Validate_UsesConstraintIdsAndCanonicalFacts_NotDishWords()
    {
        var recipeId = Guid.NewGuid();
        var request = new MealSuggestionRequest
        {
            WeekStart = Monday,
            KnownRecipes = [new RecipeOption
            {
                Id = recipeId,
                Title = "Een titel in om het even welke taal",
                FactsAssessed = true,
                ContainsClasses = [IngredientClass.Poultry]
            }]
        };
        var intent = new PlanningIntent
        {
            Constraints = [new PlanningConstraint
            {
                Id = "required-food",
                Dates = [Monday],
                RequiredClasses = [IngredientClass.Poultry]
            }]
        };
        var payload = new WeekSuggestionsPayload([new DaySuggestionPayload(
            Monday.ToString("yyyy-MM-dd"), "Opaque title", "Een titel in om het even welke taal",
            null, null, ConstraintIds: ["required-food"])]);

        PlanningIntentValidator.Validate(payload, intent, request, null).Should().BeEmpty();
    }

    [Fact]
    public void Validate_RejectsUnassessedFactsInsteadOfInferringFromTitle()
    {
        var request = new MealSuggestionRequest
        {
            WeekStart = Monday,
            KnownRecipes = [new RecipeOption { Id = Guid.NewGuid(), Title = "Chicken kip poulet" }]
        };
        var intent = new PlanningIntent
        {
            Constraints = [new PlanningConstraint
            {
                Id = "required-food",
                Dates = [Monday],
                RequiredClasses = [IngredientClass.Poultry]
            }]
        };
        var payload = new WeekSuggestionsPayload([new DaySuggestionPayload(
            Monday.ToString("yyyy-MM-dd"), "Chicken kip poulet", "Chicken kip poulet",
            null, null, ConstraintIds: ["required-food"])]);

        PlanningIntentValidator.Validate(payload, intent, request, null)
            .Should().Contain(issue => issue.Contains("unverified dietary facts"));
    }

    [Fact]
    public void Validate_AcceptsUnassessedExplicitDietOverrideWhenConstraintIsClaimed()
    {
        var request = new MealSuggestionRequest
        {
            WeekStart = Monday,
            KnownRecipes = [new RecipeOption { Id = Guid.NewGuid(), Title = "Opaque requested dish" }]
        };
        var intent = new PlanningIntent
        {
            Constraints = [new PlanningConstraint
            {
                Id = "explicit-request",
                Dates = [Monday],
                RequiredClasses = [IngredientClass.Poultry],
                OverrideDietPreferences = true,
                AllAttendees = true
            }]
        };
        var payload = new WeekSuggestionsPayload([new DaySuggestionPayload(
            Monday.ToString("yyyy-MM-dd"), "Opaque requested dish", "Opaque requested dish",
            null, null, ConstraintIds: ["explicit-request"])]);

        PlanningIntentValidator.Validate(payload, intent, request, null).Should().BeEmpty();
    }

    [Fact]
    public void ValidateAndRepair_RemoveExternalCandidatesThatDoNotClaimASourceConstraint()
    {
        var request = new MealSuggestionRequest { WeekStart = Monday };
        var intent = new PlanningIntent
        {
            Constraints = [new PlanningConstraint
            {
                Id = "source-main",
                SourceHost = "recipes.example",
                Dates = [Monday],
                Count = 1
            }]
        };
        var unclaimed = new DaySuggestionPayload(
            Monday.AddDays(1).ToString("yyyy-MM-dd"), "Extra recipe", null,
            null, "candidate-extra", ConstraintIds: []);
        var payload = new WeekSuggestionsPayload([unclaimed]);

        PlanningIntentValidator.Validate(payload, intent, request, null)
            .Should().Contain(issue => issue.Contains("does not claim a source constraint"));

        var repaired = PlanningCandidateSelector.Select([new MealSuggestion
        {
            Date = Monday.AddDays(1), DishName = "Extra recipe", SourceUrl = "https://recipes.example/extra"
        }], intent, request with { DaysToFill = [Monday, Monday.AddDays(1)] }, []);
        repaired.Suggestions.Should().NotContain(item => item.SourceUrl == "https://recipes.example/extra");
    }
}

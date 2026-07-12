using Dishhive.Api.Models;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

public class PlanningIntentValidatorTests
{
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
}

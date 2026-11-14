using Dishhive.Api.Models;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

public class PlanningCandidateSelectionTests
{
    [Fact]
    public void InsufficientAssessedFoodRetainsTheRequestedFridayAndLeavesTwoDaysEmpty()
    {
        var memberId = Guid.NewGuid();
        var friday = Monday.AddDays(4);
        var request = new MealSuggestionRequest { DaysToFill = Enumerable.Range(0, 7).Select(Monday.AddDays).ToList(),
            Members = [new MemberProfile { Id = memberId, Name = "Member", Allergies = [new DietaryTagProfile
                { Name = "Milk", ExcludedClasses = [IngredientClass.Milk] }] }],
            KnownRecipes = Enumerable.Range(0, 4).Select(index => new RecipeOption { Id = Guid.NewGuid(),
                Title = "Assessed saved food " + index, FactsAssessed = true }).ToList() };
        var intent = new PlanningIntent { Constraints = [new PlanningConstraint { Id = "friday", Dates = [friday],
            SourceHost = "example.com", Count = 1 }] };
        var proposed = request.DaysToFill.Select(date => new MealSuggestion { Date = date, DishName = "Unassessed " + date,
            AttendeeIds = [memberId] }).ToList();
        proposed[4] = new() { Date = friday, DishName = "Pancakes", SourceUrl = "https://example.com/pancakes",
            ResolvedFactsAssessed = true, AttendeeIds = [memberId], ConstraintIds = ["friday"] };
        var selected = PlanningCandidateSelector.Select(proposed, intent, request, []);
        Assert.Equal(5, selected.Suggestions.Count);
        Assert.Equal(4, selected.Suggestions.Count(item => item.RecipeId != null));
        Assert.All(selected.Suggestions, item => Assert.True(item.ResolvedFactsAssessed));
        Assert.Single(selected.Suggestions, item => item.Date == friday && item.SourceUrl != null);
        var final = PlanningIntentValidator.ValidateFinal(selected.Suggestions, intent, request);
        Assert.Equal(2, final.UnmetRequirements.Count(issue => issue.Id.StartsWith("day-")));
        Assert.DoesNotContain(final.UnmetRequirements, issue => issue.Id == "friday");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompleteSourceRequirementsStillPreferAssessedLocalFoodOnOtherDaysWhenAllergiesAreCheckable(bool canonicalAllergy)
    {
        var memberId = Guid.NewGuid();
        var localId = Guid.NewGuid();
        var request = new MealSuggestionRequest { DaysToFill = [Monday, Monday.AddDays(1)], Members =
            [new MemberProfile { Id = memberId, Name = "Member", Allergies = [new DietaryTagProfile
                { Name = "Restriction", ExcludedClasses = canonicalAllergy ? [IngredientClass.Milk] : [] }] }],
            KnownRecipes = [new RecipeOption { Id = localId, Title = "Assessed saved dish", FactsAssessed = true }] };
        var intent = new PlanningIntent { Constraints = [new PlanningConstraint { Id = "source", Dates = [Monday],
            SourceHost = "example.com", Count = 1 }] };
        MealSuggestion[] proposed = [
            new() { Date = Monday, SourceUrl = "https://example.com/a", DishName = "Source dish", ResolvedFactsAssessed = true,
                AttendeeIds = [memberId], ConstraintIds = ["source"] },
            new() { Date = Monday.AddDays(1), DishName = "Unverified plain dish", AttendeeIds = [memberId],
                AllergyWarning = "Review unknown ingredients" }];
        var selected = PlanningCandidateSelector.Select(proposed, intent, request, []);
        Assert.Equal(2, selected.Suggestions.Count);
        Assert.Equal("https://example.com/a", selected.Suggestions[0].SourceUrl);
        Assert.Equal(canonicalAllergy ? localId : (Guid?)null, selected.Suggestions[1].RecipeId);
        Assert.Empty(PlanningIntentValidator.ValidateFinal(selected.Suggestions, intent, request).UnmetRequirements);
    }

    private static readonly DateOnly Monday = new(2026, 11, 16);

    [Fact]
    public void Selection_CannotReuseADessertResearchPoolAsAMainFromTheSameHost()
    {
        var request = new MealSuggestionRequest { DaysToFill = [Monday] };
        var intent = new PlanningIntent { Constraints = [
            new PlanningConstraint { Id = "main", Dates = [Monday], SourceHost = "recipes.example" },
            new PlanningConstraint { Id = "dessert", Dates = [Monday], SourceHost = "recipes.example", Course = Course.Dessert }
        ] };
        var candidate = new ExternalRecipeCandidate("dessert-id", "https://recipes.example/dessert", "Dessert",
            ["ingredient"], ["instruction"], null, [], true)
        { ResearchConstraintIds = ["dessert"] };

        var selected = PlanningCandidateSelector.Select([], intent, request, [candidate]);

        selected.Suggestions.Should().ContainSingle().Which.Course.Should().Be(Course.Dessert);
        PlanningIntentValidator.ValidateFinal(selected.Suggestions, intent, request).UnmetRequirements
            .Should().Contain(issue => issue.Id == "main");
    }

    [Fact]
    public void Selection_CombinesMembershipTimeRecipeIdentityAllergyAndAttendeesWithoutReusingScarceCandidates()
    {
        var ordinary = new RecipeOption { Id = Guid.NewGuid(), Title = "Same title", FactsAssessed = true, TotalTimeMinutes = 30 };
        var scarce = new RecipeOption { Id = Guid.NewGuid(), Title = "Same title", FactsAssessed = true,
            TotalTimeMinutes = 10, ContainsClasses = [IngredientClass.Poultry] };
        var unsafeRecipe = new RecipeOption { Id = Guid.NewGuid(), Title = "Tempting first pick", FactsAssessed = true,
            TotalTimeMinutes = 5, ContainsClasses = [IngredientClass.Milk, IngredientClass.Poultry] };
        var attendees = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var request = new MealSuggestionRequest
        {
            DaysToFill = [Monday, Monday.AddDays(1)], KnownRecipes = [unsafeRecipe, scarce, ordinary],
            Members = attendees.Select(id => new MemberProfile { Id = id, Name = "Attendee", Allergies =
                [new DietaryTagProfile { Name = "Allergy", ExcludedClasses = [IngredientClass.Milk] }] }).ToList(),
            CollectionConstraints = [new CollectionConstraint
            {
                Id = "required", Name = "Required", RecipeIds = [ordinary.Id, scarce.Id, unsafeRecipe.Id], IsGlobal = true
            }]
        };
        var intent = new PlanningIntent { MaxFreezerMeals = 0, Constraints = [
            new PlanningConstraint { Id = "broad", Dates = [Monday], RequiredCollectionIds = ["required"],
                AllowedOrigins = [PlanningOrigin.LocalRecipe], MaxTotalTimeMinutes = 30 },
            new PlanningConstraint { Id = "scarce", Dates = [Monday.AddDays(1)], RequiredCollectionIds = ["required"],
                RequiredRecipeIds = [scarce.Id], RequiredClasses = [IngredientClass.Poultry],
                AllowedOrigins = [PlanningOrigin.LocalRecipe], MaxTotalTimeMinutes = 15 }
        ] };

        var selected = PlanningCandidateSelector.Select([], intent, request, []);

        selected.Suggestions.Select(item => item.RecipeId).Should().Equal(ordinary.Id, scarce.Id);
        selected.Suggestions.Should().OnlyContain(item => item.AttendeeIds.Count == 2);
        PlanningIntentValidator.ValidateFinal(selected.Suggestions, intent, request).UnmetRequirements.Should().BeEmpty();
    }

    [Fact]
    public void Selection_DoesNotCycleCandidatesToPretendAnUnavailableCountWasSatisfied()
    {
        var recipe = new RecipeOption { Id = Guid.NewGuid(), Title = "Only candidate", FactsAssessed = true };
        var request = new MealSuggestionRequest { DaysToFill = [Monday, Monday.AddDays(1)], KnownRecipes = [recipe] };
        var intent = new PlanningIntent { AllowRepeatedDishes = true, Constraints = [new PlanningConstraint
        {
            Id = "two-distinct", Dates = request.DaysToFill, Count = 2, Distinct = true,
            AllowedOrigins = [PlanningOrigin.LocalRecipe]
        }] };

        var selected = PlanningCandidateSelector.Select([], intent, request, []);

        selected.Suggestions.Should().ContainSingle();
        PlanningIntentValidator.ValidateFinal(selected.Suggestions, intent, request).UnmetRequirements
            .Should().Contain(issue => issue.Id == "two-distinct");
    }

    [Fact]
    public void Selection_ReportsMissingFactsWithoutCallingThemAModelFailure()
    {
        var request = new MealSuggestionRequest { DaysToFill = [Monday], KnownRecipes =
            [new RecipeOption { Id = Guid.NewGuid(), Title = "Unknown ingredients" }] };
        var intent = new PlanningIntent { Constraints = [new PlanningConstraint
        {
            Id = "verified-food", Dates = [Monday], RequiredClasses = [IngredientClass.Poultry],
            AllowedOrigins = [PlanningOrigin.LocalRecipe]
        }] };

        var selected = PlanningCandidateSelector.Select([], intent, request, []);

        selected.Suggestions.Should().BeEmpty();
        selected.Diagnostics.Should().ContainSingle().Which.Id.Should().Be("facts-verified-food");
        selected.Diagnostics.Should().NotContain(issue => issue.Id == "model-quality");
    }

    [Fact]
    public void Selection_ClaimsCompatibleOverlappingRequirementsTogether()
    {
        var recipe = new RecipeOption { Id = Guid.NewGuid(), Title = "Shared main", FactsAssessed = true, TotalTimeMinutes = 10 };
        var request = new MealSuggestionRequest { DaysToFill = [Monday], KnownRecipes = [recipe] };
        var intent = new PlanningIntent { Constraints = [
            new PlanningConstraint { Id = "saved", Dates = [Monday], AllowedOrigins = [PlanningOrigin.LocalRecipe] },
            new PlanningConstraint { Id = "quick", Dates = [Monday], MaxTotalTimeMinutes = 15 }
        ] };

        var selected = PlanningCandidateSelector.Select([], intent, request, []);

        selected.Suggestions.Should().ContainSingle().Which.ConstraintIds.Should().BeEquivalentTo("saved", "quick");
        PlanningIntentValidator.ValidateFinal(selected.Suggestions, intent, request).UnmetRequirements.Should().BeEmpty();
    }

    [Fact]
    public void FinalValidation_CollectionMembershipUsesIdsEvenWhenTitlesMatch()
    {
        var memberId = Guid.NewGuid();
        var impostorId = Guid.NewGuid();
        var request = new MealSuggestionRequest
        {
            DaysToFill = [Monday],
            CollectionConstraints = [new CollectionConstraint
            {
                Id = "collection", Name = "Required", RecipeIds = [memberId],
                RecipeTitles = ["Same title"], Dates = [Monday]
            }]
        };
        var suggestion = new MealSuggestion { Date = Monday, RecipeId = impostorId, DishName = "Same title" };

        var result = PlanningIntentValidator.ValidateFinal([suggestion], new PlanningIntent(), request);

        result.Suggestions.Should().BeEmpty();
        result.UnmetRequirements.Should().Contain(issue => issue.Id == "collection-Required");
    }

    [Fact]
    public void FinalValidation_GlobalMandatoryCollectionCannotBeSatisfiedByAnUnlinkedName()
    {
        var intent = new PlanningIntent { Constraints = [new PlanningConstraint
        {
            Id = "mandatory", Dates = [Monday], RequiredCollectionIds = ["collection"]
        }] };
        var request = new MealSuggestionRequest
        {
            DaysToFill = [Monday], CollectionConstraints = [new CollectionConstraint
            {
                Id = "collection", Name = "Required", RecipeIds = [Guid.NewGuid()],
                RecipeTitles = ["Same title"], IsGlobal = true
            }]
        };
        var suggestion = new MealSuggestion { Date = Monday, DishName = "Same title", ConstraintIds = ["mandatory"] };

        var result = PlanningIntentValidator.ValidateFinal([suggestion], intent, request);

        result.Suggestions.Should().BeEmpty();
        result.UnmetRequirements.Should().Contain(issue => issue.Id == "mandatory");
    }

    [Fact]
    public void FinalValidation_DatedMainCollectionDoesNotConstrainADessert()
    {
        var recipeId = Guid.NewGuid();
        var request = new MealSuggestionRequest
        {
            DaysToFill = [Monday], CollectionConstraints = [new CollectionConstraint
            {
                Id = "collection", Name = "Required", RecipeIds = [recipeId],
                RecipeTitles = ["Main"], Slots = [new(Monday)]
            }]
        };
        var result = PlanningIntentValidator.ValidateFinal([
            new MealSuggestion { Date = Monday, RecipeId = recipeId, DishName = "Renamed main" },
            new MealSuggestion { Date = Monday, DishName = "Dessert", Course = Course.Dessert }
        ], new PlanningIntent(), request);

        result.Suggestions.Should().HaveCount(2);
        result.UnmetRequirements.Should().BeEmpty();
    }
}

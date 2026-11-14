using System.Net.Http.Json;
using System.Text.Json;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Suggestions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dishhive.Api.Tests.Integration;

public sealed class AiReviewTelemetryIntegrationTests : TestBase
{
    private Recipe AssessedRecipe()
    {
        var recipe = new Recipe { Title = "Private recipe title", Revision = 3, FactsConfirmationVersion = 2,
            DietaryFactsStatus = DietaryFactsStatus.AiDetected, DietaryFactsAssessedRevision = 3,
            DietaryFactsAssessedAt = DateTime.UtcNow, Ingredients = [new() { Name = "Ingredient", SortOrder = 0 }],
            DietaryFacts = [new() { IngredientClass = IngredientClass.Milk }] };
        DbContext.Recipes.Add(recipe);
        DbContext.RecipeEnrichmentJobs.Add(new() { Recipe = recipe, RecipeRevision = 3, FactsConfirmationVersion = 2,
            OperationId = "facts-source", Task = RecipeEnrichmentTask.Facts, State = RecipeEnrichmentJobState.Completed });
        DbContext.AiTaskRuns.Add(new() { OperationId = "facts-source", Task = "recipe.facts", Model = "review-model",
            ConfigFingerprint = "cohort", StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow });
        DbContext.SaveChanges();
        return recipe;
    }

    [Fact]
    public async Task TranslationTextEdit_IsLinkedToTheRecordedTranslationTask()
    {
        var recipe = AssessedRecipe();
        recipe.ContentLanguage = "nl";
        DbContext.RecipeEnrichmentJobs.Add(new() { Recipe = recipe, RecipeRevision = 3,
            Task = RecipeEnrichmentTask.Translation, State = RecipeEnrichmentJobState.Completed,
            TargetLanguage = "nl", OperationId = "translation-source" });
        DbContext.AiTaskRuns.Add(new() { OperationId = "translation-source", Task = "recipe.translate",
            Model = "translation-model", StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow });
        await DbContext.SaveChangesAsync();
        (await Client.PutAsJsonAsync($"/api/recipes/{recipe.Id}", new UpdateRecipeDto {
            Title = "Edited translation", ContentLanguage = "nl", Ingredients = [new() { Name = "Ingredient" }]
        })).EnsureSuccessStatusCode();
        var metrics = await Client.GetFromJsonAsync<AiPlanningMetricsResponseDto>(
            "/api/integrations/ai/planning-runs?task=recipe.translate&model=translation-model");
        Assert.Equal(1, metrics!.Reviews!.TextEditsAfterTranslation);
        Assert.Equal("translation-source", Assert.Single(metrics.Reviews.Events).OperationId);
    }

    [Fact]
    public async Task SameSourceFactCorrection_HasVersionsAndCohort_AndSubsequentConfirmationIsSeparate()
    {
        var recipe = AssessedRecipe();
        (await Client.PutAsJsonAsync($"/api/recipes/{recipe.Id}/facts", new { contains = new[] { "Gluten" } })).EnsureSuccessStatusCode();
        (await Client.PutAsJsonAsync($"/api/recipes/{recipe.Id}/facts", new { contains = new[] { "Gluten" } })).EnsureSuccessStatusCode();
        var metrics = await Client.GetFromJsonAsync<AiPlanningMetricsResponseDto>("/api/integrations/ai/planning-runs?model=review-model&fingerprint=cohort");
        var reviews = metrics!.Reviews!;
        Assert.Equal(1, reviews.FactsCorrections);
        Assert.Equal(1, reviews.FactsReviews); // Only the first action has this recorded model operation.
        var correction = Assert.Single(reviews.Events);
        Assert.Equal("factsCorrected", correction.Kind);
        Assert.Equal(3, correction.SourceRevision);
        Assert.Equal(3, correction.ObservedRevision);
        Assert.Equal(2, correction.SourceConfirmationVersion);
        Assert.Equal(3, correction.ObservedConfirmationVersion);
        Assert.Equal(new[] { "facts" }, correction.ChangedFields);
        Assert.DoesNotContain("Private recipe title", JsonSerializer.Serialize(reviews));
        var all = await Client.GetFromJsonAsync<AiPlanningMetricsResponseDto>("/api/integrations/ai/planning-runs");
        Assert.Equal(2, all!.Reviews!.FactsReviews);
        Assert.Equal(1, all.Reviews.FactsCorrections);
    }

    [Fact]
    public async Task FactsOnChangedSource_AreNotCountedAsCorrectionOfTheOldModelInput()
    {
        var recipe = AssessedRecipe();
        var response = await Client.PutAsJsonAsync($"/api/recipes/{recipe.Id}", new UpdateRecipeDto
        {
            Title = "New source", Ingredients = [new() { Name = "Different ingredient" }], ContainsClasses = ["Gluten"]
        });
        response.EnsureSuccessStatusCode();
        using var check = CreateFreshContext();
        var events = await check.AiReviewEvents.ToListAsync();
        Assert.Contains(events, review => review.Kind == "factsSetAfterSourceEdit" && review.SourceRevision == 3 && review.ObservedRevision == 4);
        Assert.DoesNotContain(events, review => review.Kind == "factsCorrected");
    }

    [Fact]
    public async Task SuggestionEditsAreStructural_AndRetriesPreserveBothReviewAttempts()
    {
        var batch = Guid.NewGuid();
        var suggestion = new MealSuggestionDto { Id = Guid.NewGuid(), Date = DateOnly.FromDateTime(DateTime.Today).AddDays(2), DishName = "Original dinner" };
        using (var scope = Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<MealSuggestionAcceptanceService>().StoreBatchAsync(
                batch, PlanCompletionStatus.Complete, [suggestion], operationId: "plan-source");
        var item = new AcceptMealSuggestionDto { Id = suggestion.Id, Date = suggestion.Date, DishName = "Edited dinner" };
        async Task Accept() => (await Client.PostAsJsonAsync("/api/plannedmeals/suggestions/accept",
            new AcceptMealSuggestionsRequestDto { BatchId = batch, Suggestions = [item] })).EnsureSuccessStatusCode();
        await Accept();
        item.DishName = suggestion.DishName;
        await Accept();
        using var check = CreateFreshContext();
        var events = await check.AiReviewEvents.Where(review => review.SuggestionId == suggestion.Id).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.Contains(events, review => review.ChangedFieldsJson == "[\"dishName\"]" && review.Outcome == "needsReview");
        Assert.Contains(events, review => review.ChangedFieldsJson == "[]" && review.Outcome == "accepted");
        Assert.True((await check.MealSuggestionCandidates.SingleAsync()).WasModified);
        var metrics = await Client.GetFromJsonAsync<AiPlanningMetricsResponseDto>("/api/integrations/ai/planning-runs?operationId=plan-source");
        Assert.Equal(1, metrics!.Reviews!.ReviewedSuggestions);
        Assert.Equal(1, metrics.Reviews.EditedSuggestions);
    }

    [Fact]
    public async Task RecipeChangedAfterReview_IsRecordedAsInputChangeWithoutClaimingASubmittedEdit()
    {
        var recipe = AssessedRecipe();
        var batch = Guid.NewGuid();
        var suggestion = new MealSuggestionDto { Id = Guid.NewGuid(), Date = DateOnly.FromDateTime(DateTime.Today).AddDays(2),
            DishName = recipe.Title, RecipeId = recipe.Id, CandidateVersion = "local:3:2" };
        using (var scope = Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<MealSuggestionAcceptanceService>().StoreBatchAsync(
                batch, PlanCompletionStatus.Complete, [suggestion]);
        recipe.Revision++;
        await DbContext.SaveChangesAsync();
        (await Client.PostAsJsonAsync("/api/plannedmeals/suggestions/accept", new AcceptMealSuggestionsRequestDto
        {
            BatchId = batch, Suggestions = [new() { Id = suggestion.Id, Date = suggestion.Date, DishName = suggestion.DishName,
                RecipeId = recipe.Id, CandidateVersion = suggestion.CandidateVersion }]
        })).EnsureSuccessStatusCode();
        using var check = CreateFreshContext();
        var review = await check.AiReviewEvents.SingleAsync();
        Assert.Equal("needsReview", review.Outcome);
        Assert.Equal("[]", review.ChangedFieldsJson);
        Assert.Contains("recipeRevision", review.InputChangesJson);
        Assert.Equal(3, review.SourceRevision);
        Assert.Equal(4, review.ObservedRevision);
    }
}

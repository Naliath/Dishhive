using System.Net;
using System.Net.Http.Json;
using System.Text;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Tests.Integration;

/// <summary>
/// Recipe dietary facts: the explicit facts editor endpoint, the library status/
/// backfill endpoints (AI is unconfigured in Testing, so the queue is a visible
/// no-op) and the dishhive:dietaryFacts export/import round trip.
/// </summary>
public class RecipeFactsIntegrationTests : TestBase
{
    private async Task<RecipeDto> CreateRecipeAsync(string title = "Testgerecht", List<string>? containsClasses = null)
    {
        var response = await Client.PostAsJsonAsync("/api/recipes", new CreateRecipeDto
        {
            Title = title,
            Ingredients = [new CreateRecipeIngredientDto { Name = "boter" }],
            ContainsClasses = containsClasses
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<RecipeDto>())!;
    }

    [Fact]
    public async Task CreateRecipe_WithoutClasses_StaysUnassessed()
    {
        // AI is unconfigured in Testing: the enqueue is a no-op, nothing pretends
        // the recipe was assessed
        var created = await CreateRecipeAsync();

        created.DietaryFacts.Status.Should().Be(DietaryFactsStatus.Unassessed);
        created.DietaryFacts.Contains.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRecipe_WithExplicitClasses_IsUserConfirmed()
    {
        var created = await CreateRecipeAsync(containsClasses: ["Milk", "Gluten"]);

        created.DietaryFacts.Status.Should().Be(DietaryFactsStatus.UserConfirmed);
        created.DietaryFacts.Contains.Should().BeEquivalentTo("Milk", "Gluten");
        created.DietaryFacts.AssessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SetFacts_ReplacesClassesAndConfirms()
    {
        var created = await CreateRecipeAsync(containsClasses: ["Milk"]);

        var response = await Client.PutAsJsonAsync($"/api/recipes/{created.Id}/facts",
            new UpdateRecipeFactsDto { Contains = ["TreeNuts", "Eggs"] });
        var facts = await response.Content.ReadFromJsonAsync<RecipeDietaryFactsDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        facts!.Status.Should().Be(DietaryFactsStatus.UserConfirmed);
        facts.Contains.Should().BeEquivalentTo("TreeNuts", "Eggs");

        var reloaded = await Client.GetFromJsonAsync<RecipeDto>($"/api/recipes/{created.Id}");
        reloaded!.DietaryFacts.Contains.Should().BeEquivalentTo("TreeNuts", "Eggs");
    }

    [Fact]
    public async Task SetFacts_UnknownClassName_ReturnsBadRequest()
    {
        var created = await CreateRecipeAsync();

        var response = await Client.PutAsJsonAsync($"/api/recipes/{created.Id}/facts",
            new UpdateRecipeFactsDto { Contains = ["Kryptonite"] });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetFacts_UnknownRecipe_ReturnsNotFound()
    {
        var response = await Client.PutAsJsonAsync($"/api/recipes/{Guid.NewGuid()}/facts",
            new UpdateRecipeFactsDto { Contains = [] });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FactsStatus_CountsByAssessmentState()
    {
        await CreateRecipeAsync("Onbeoordeeld");
        await CreateRecipeAsync("Bevestigd", containsClasses: ["Fish"]);

        var status = await Client.GetFromJsonAsync<RecipeFactsStatusDto>("/api/recipes/facts/status");

        status!.Unassessed.Should().Be(1);
        status.UserConfirmed.Should().Be(1);
        status.AiDetected.Should().Be(0);
        status.Available.Should().BeFalse("AI is not configured in the Testing environment");
        status.Running.Should().BeFalse();
    }

    [Fact]
    public async Task Backfill_WithoutAi_EnqueuesNothing()
    {
        await CreateRecipeAsync("Onbeoordeeld");

        var response = await Client.PostAsJsonAsync("/api/recipes/facts/backfill", new { });
        var result = await response.Content.ReadFromJsonAsync<RecipeFactsBackfillResultDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result!.Enqueued.Should().Be(0, "the NoOp extractor makes enqueueing a visible no-op");
    }

    [Fact]
    public async Task UpdateRecipe_ChangedIngredients_ResetsNothingWhenAiUnavailable()
    {
        // With AI unconfigured the re-assessment enqueue no-ops; the stale-but-
        // confirmed facts stay (better than dropping data with no way to recompute)
        var created = await CreateRecipeAsync(containsClasses: ["Milk"]);

        var update = new UpdateRecipeDto
        {
            Title = created.Title,
            Ingredients = [new CreateRecipeIngredientDto { Name = "olijfolie" }]
        };
        var response = await Client.PutAsJsonAsync($"/api/recipes/{created.Id}", update);
        var updated = await response.Content.ReadFromJsonAsync<RecipeDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.DietaryFacts.Status.Should().Be(DietaryFactsStatus.UserConfirmed);
        updated.DietaryFacts.Contains.Should().BeEquivalentTo("Milk");
    }

    [Fact]
    public async Task ExportImport_RoundTripsDietaryFacts()
    {
        var created = await CreateRecipeAsync("Notencake", containsClasses: ["TreeNuts", "Milk"]);

        var exportJson = await Client.GetStringAsync("/api/recipes/export");
        exportJson.Should().Contain("dishhive:dietaryFacts");

        // Import into an empty library
        using var fresh = CreateFreshContext();
        fresh.RecipeDietaryFacts.RemoveRange(fresh.RecipeDietaryFacts);
        fresh.RecipeIngredients.RemoveRange(fresh.RecipeIngredients);
        fresh.RecipeSteps.RemoveRange(fresh.RecipeSteps);
        fresh.Recipes.RemoveRange(fresh.Recipes);
        await fresh.SaveChangesAsync();

        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(exportJson)), "file", "export.json" }
        };
        var importResponse = await Client.PostAsync("/api/recipes/import/file", content);
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var assertContext = CreateFreshContext();
        var imported = await assertContext.Recipes
            .Include(r => r.DietaryFacts)
            .SingleAsync(r => r.Title == "Notencake");
        imported.DietaryFactsStatus.Should().Be(DietaryFactsStatus.UserConfirmed);
        imported.DietaryFacts.Select(f => f.IngredientClass)
            .Should().BeEquivalentTo([IngredientClass.TreeNuts, IngredientClass.Milk]);
    }
}

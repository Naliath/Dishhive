using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;
using Xunit;

namespace Dishhive.Api.Tests.Integration;

public class RecipesControllerIntegrationTests : TestBase
{
    public RecipesControllerIntegrationTests()
    {
        ClearDatabase();
    }

    private static object BasicRecipePayload(string title = "Pasta Carbonara", string description = "Classic Italian pasta.") => new
    {
        title,
        description,
        servings = 4,
        prepTimeMinutes = 10,
        cookTimeMinutes = 20,
        tags = new[] { "Italian", "Pasta" },
        ingredients = new[]
        {
            new { name = "Spaghetti", quantity = 400.0, unit = (string?)"g", sortOrder = 0 },
            new { name = "Eggs", quantity = 3.0, unit = (string?)null, sortOrder = 1 }
        },
        steps = new[]
        {
            new { stepNumber = 1, instruction = "Cook spaghetti." },
            new { stepNumber = 2, instruction = "Mix eggs and cheese." }
        }
    };

    [Fact]
    public async Task GetAll_WhenNoRecipes_ReturnsEmptyList()
    {
        var resp = await Client.GetAsync("/api/recipes");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await resp.Content.ReadFromJsonAsync<List<RecipeDtos.RecipeSummaryDto>>();
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ValidRecipe_ReturnsCreated()
    {
        var resp = await Client.PostAsJsonAsync("/api/recipes", BasicRecipePayload());
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await resp.Content.ReadFromJsonAsync<RecipeDtos.RecipeDto>();
        dto!.Title.Should().Be("Pasta Carbonara");
        dto.Ingredients.Should().HaveCount(2);
        dto.Steps.Should().HaveCount(2);
        dto.Tags.Should().Contain("Italian");
    }

    [Fact]
    public async Task GetById_AfterCreate_ReturnsRecipe()
    {
        var created = await (await Client.PostAsJsonAsync("/api/recipes", BasicRecipePayload()))
            .Content.ReadFromJsonAsync<RecipeDtos.RecipeDto>();

        var resp = await Client.GetAsync($"/api/recipes/{created!.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.Content.ReadFromJsonAsync<RecipeDtos.RecipeDto>();
        dto!.Id.Should().Be(created.Id);
        dto.Ingredients.Should().HaveCount(2);
        dto.Steps.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var resp = await Client.GetAsync($"/api/recipes/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAll_ReturnsMultipleRecipes()
    {
        await Client.PostAsJsonAsync("/api/recipes", BasicRecipePayload("Recipe A"));
        await Client.PostAsJsonAsync("/api/recipes", BasicRecipePayload("Recipe B"));

        var resp = await Client.GetAsync("/api/recipes");
        var list = await resp.Content.ReadFromJsonAsync<List<RecipeDtos.RecipeSummaryDto>>();
        list.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAll_WithSearchQuery_FiltersResults()
    {
        await Client.PostAsJsonAsync("/api/recipes", BasicRecipePayload("Pasta Bolognese"));
        await Client.PostAsJsonAsync("/api/recipes", BasicRecipePayload("Chicken Tikka", "Spicy Indian dish."));

        var resp = await Client.GetAsync("/api/recipes?search=pasta");
        var list = await resp.Content.ReadFromJsonAsync<List<RecipeDtos.RecipeSummaryDto>>();
        list.Should().HaveCount(1).And.ContainSingle(r => r.Title == "Pasta Bolognese");
    }

    [Fact]
    public async Task Update_ChangesRecipeFields()
    {
        var created = await (await Client.PostAsJsonAsync("/api/recipes", BasicRecipePayload()))
            .Content.ReadFromJsonAsync<RecipeDtos.RecipeDto>();

        var updateResp = await Client.PutAsJsonAsync($"/api/recipes/{created!.Id}", new
        {
            title = "Updated Carbonara",
            description = "Updated description.",
            servings = 6,
            tags = new[] { "Italian" },
            ingredients = new[] { new { name = "Spaghetti", quantity = 500.0, unit = "g", sortOrder = 0 } },
            steps = new[] { new { stepNumber = 1, instruction = "Cook pasta." } }
        });
        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var dto = await (await Client.GetAsync($"/api/recipes/{created.Id}"))
            .Content.ReadFromJsonAsync<RecipeDtos.RecipeDto>();
        dto!.Title.Should().Be("Updated Carbonara");
        dto.Servings.Should().Be(6);
        dto.Ingredients.Should().HaveCount(1);
    }

    [Fact]
    public async Task Delete_RemovesRecipe()
    {
        var created = await (await Client.PostAsJsonAsync("/api/recipes", BasicRecipePayload()))
            .Content.ReadFromJsonAsync<RecipeDtos.RecipeDto>();

        var del = await Client.DeleteAsync($"/api/recipes/{created!.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await Client.GetAsync($"/api/recipes/{created.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/recipes/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Import_WithUnsupportedUrl_Returns400()
    {
        var resp = await Client.PostAsJsonAsync("/api/recipes/import",
            new { url = "https://www.example.com/some-recipe" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

public class RecipesControllerIntegrationTests : TestBase
{
    private static CreateRecipeDto SampleRecipe() => new()
    {
        Title = "Spaghetti bolognese",
        Description = "Klassieker",
        Servings = 4,
        PrepTimeMinutes = 15,
        CookTimeMinutes = 45,
        Category = "Pasta",
        Keywords = "italiaans, comfort",
        Ingredients =
        {
            new CreateRecipeIngredientDto { Name = "gehakt", Quantity = 500, Unit = "g" },
            new CreateRecipeIngredientDto { Name = "spaghetti", Quantity = 400, Unit = "g" }
        },
        Steps =
        {
            new CreateRecipeStepDto { Instruction = "Bak het gehakt." },
            new CreateRecipeStepDto { Instruction = "Kook de spaghetti." }
        }
    };

    [Fact]
    public async Task CreateRecipe_PersistsIngredientsAndStepsInOrder()
    {
        var response = await Client.PostAsJsonAsync("/api/recipes", SampleRecipe());
        var created = await response.Content.ReadFromJsonAsync<RecipeDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.Title.Should().Be("Spaghetti bolognese");
        created.Ingredients.Should().HaveCount(2);
        created.Ingredients[0].Name.Should().Be("gehakt");
        created.Steps.Should().HaveCount(2);
        created.Steps[0].StepNumber.Should().Be(1);
        created.Steps[0].Instruction.Should().Be("Bak het gehakt.");
    }

    [Fact]
    public async Task CreateRecipe_WithoutTitle_ReturnsBadRequest()
    {
        var dto = SampleRecipe();
        dto.Title = "";

        var response = await Client.PostAsJsonAsync("/api/recipes", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetRecipes_FiltersByTitleSearch()
    {
        DbContext.Recipes.AddRange(
            new Recipe { Title = "Vispannetje" },
            new Recipe { Title = "Stoofvlees" });
        await DbContext.SaveChangesAsync();

        var results = await Client.GetFromJsonAsync<List<RecipeListItemDto>>("/api/recipes?search=vis");

        results.Should().ContainSingle(r => r.Title == "Vispannetje");
    }

    [Fact]
    public async Task GetRecipes_FiltersByKeywords()
    {
        DbContext.Recipes.Add(new Recipe { Title = "Dessertje", Keywords = "Zoet, Zomer" });
        await DbContext.SaveChangesAsync();

        var results = await Client.GetFromJsonAsync<List<RecipeListItemDto>>("/api/recipes?search=zomer");

        results.Should().ContainSingle(r => r.Title == "Dessertje");
    }

    [Fact]
    public async Task GetIngredientNames_ReturnsDistinctNamesAcrossRecipes()
    {
        DbContext.Recipes.AddRange(
            new Recipe
            {
                Title = "Omelet",
                Ingredients = { new RecipeIngredient { Name = "ei" }, new RecipeIngredient { Name = "boter" } }
            },
            new Recipe
            {
                Title = "Pannenkoeken",
                Ingredients = { new RecipeIngredient { Name = "Ei" }, new RecipeIngredient { Name = "bloem" } }
            });
        await DbContext.SaveChangesAsync();

        var names = await Client.GetFromJsonAsync<List<string>>("/api/recipes/ingredients");

        // case variants collapse to one entry for the autocomplete
        names.Should().HaveCount(3);
        names.Should().ContainSingle(n => n.Equals("ei", StringComparison.OrdinalIgnoreCase));
        names.Should().Contain("boter").And.Contain("bloem");
    }

    [Fact]
    public async Task GetRecipe_UnknownId_ReturnsNotFound()
    {
        var response = await Client.GetAsync($"/api/recipes/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateRecipe_ReplacesIngredientsAndStepsWholesale()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/recipes", SampleRecipe());
        var created = await createResponse.Content.ReadFromJsonAsync<RecipeDto>();

        var update = new UpdateRecipeDto
        {
            Title = "Spaghetti deluxe",
            Servings = 6,
            Ingredients = { new CreateRecipeIngredientDto { Name = "truffel", Quantity = 10, Unit = "g" } },
            Steps = { new CreateRecipeStepDto { Instruction = "Alles tegelijk." } }
        };

        var response = await Client.PutAsJsonAsync($"/api/recipes/{created!.Id}", update);
        var updated = await response.Content.ReadFromJsonAsync<RecipeDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.Title.Should().Be("Spaghetti deluxe");
        updated.Servings.Should().Be(6);
        updated.Ingredients.Should().ContainSingle(i => i.Name == "truffel");
        updated.Steps.Should().ContainSingle(s => s.Instruction == "Alles tegelijk.");

        using var freshContext = CreateFreshContext();
        freshContext.RecipeIngredients.Count(i => i.RecipeId == created.Id).Should().Be(1);
        freshContext.RecipeSteps.Count(s => s.RecipeId == created.Id).Should().Be(1);
    }

    [Fact]
    public async Task DeleteRecipe_RemovesRecipeAndChildren()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/recipes", SampleRecipe());
        var created = await createResponse.Content.ReadFromJsonAsync<RecipeDto>();

        var response = await Client.DeleteAsync($"/api/recipes/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var freshContext = CreateFreshContext();
        freshContext.Recipes.Should().NotContain(r => r.Id == created.Id);
        freshContext.RecipeIngredients.Should().NotContain(i => i.RecipeId == created.Id);
    }

    [Fact]
    public async Task DeleteRecipe_PlannedMealKeepsDenormalizedDishName()
    {
        var recipe = new Recipe { Title = "Verdwijnende lasagne" };
        var meal = new PlannedMeal
        {
            Date = new DateOnly(2026, 6, 15),
            Recipe = recipe,
            DishName = recipe.Title
        };
        DbContext.PlannedMeals.Add(meal);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var response = await Client.DeleteAsync($"/api/recipes/{recipe.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var freshContext = CreateFreshContext();
        var storedMeal = freshContext.PlannedMeals.Single(m => m.Id == meal.Id);
        storedMeal.DishName.Should().Be("Verdwijnende lasagne");
        storedMeal.RecipeId.Should().BeNull();
    }
}

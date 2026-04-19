using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Dishhive.Api.Tests.Integration;

public class ShoppingListIntegrationTests : TestBase
{
    public ShoppingListIntegrationTests()
    {
        ClearDatabase();
    }

    [Fact]
    public async Task GetShoppingList_WhenNoPlanExists_Returns404()
    {
        var resp = await Client.GetAsync("/api/weekplanner/2025-03-03/shopping-list");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetShoppingList_WithNoMeals_ReturnsEmptyList()
    {
        await Client.PostAsJsonAsync("/api/weekplanner", new { weekStartDate = "2025-03-10" });

        var resp = await Client.GetAsync("/api/weekplanner/2025-03-10/shopping-list");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task GetShoppingList_AggregatesIngredientsFromPlannedRecipes()
    {
        // Create recipe with known ingredients
        var recipeResp = await Client.PostAsJsonAsync("/api/recipes", new
        {
            title = "Pasta Carbonara",
            servings = 4,
            ingredients = new[]
            {
                new { name = "Spaghetti", quantity = 400.0, unit = (string?)"g", sortOrder = 0 },
                new { name = "Eggs", quantity = 3.0, unit = (string?)null, sortOrder = 1 },
                new { name = "Parmesan", quantity = 100.0, unit = (string?)"g", sortOrder = 2 }
            },
            steps = Array.Empty<object>()
        });
        var recipeDoc = await recipeResp.Content.ReadFromJsonAsync<JsonElement>();
        var recipeId = recipeDoc.GetProperty("id").GetString();

        // Create week plan and add the recipe as a meal
        var planResp = await Client.PostAsJsonAsync("/api/weekplanner", new { weekStartDate = "2025-03-17" });
        var plan = await planResp.Content.ReadFromJsonAsync<JsonElement>();
        var planId = plan.GetProperty("id").GetString();

        await Client.PostAsJsonAsync($"/api/weekplanner/{planId}/meals", new
        {
            dayOfWeek = "Monday",
            mealType = "Dinner",
            recipeId
        });

        var resp = await Client.GetAsync("/api/weekplanner/2025-03-17/shopping-list");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        list.Should().HaveCount(3);

        var names = list!.Select(i => i.GetProperty("name").GetString()).ToList();
        names.Should().Contain("Spaghetti");
        names.Should().Contain("Eggs");
        names.Should().Contain("Parmesan");
    }

    [Fact]
    public async Task GetShoppingList_AggregatesSameIngredientAcrossRecipes()
    {
        // Two recipes both use Onion
        var r1Resp = await Client.PostAsJsonAsync("/api/recipes", new
        {
            title = "Soup",
            servings = 4,
            ingredients = new[] { new { name = "Onion", quantity = 1.0, unit = (string?)null, sortOrder = 0 } },
            steps = Array.Empty<object>()
        });
        var r1Id = (await r1Resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var r2Resp = await Client.PostAsJsonAsync("/api/recipes", new
        {
            title = "Stew",
            servings = 4,
            ingredients = new[] { new { name = "Onion", quantity = 2.0, unit = (string?)null, sortOrder = 0 } },
            steps = Array.Empty<object>()
        });
        var r2Id = (await r2Resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var planResp = await Client.PostAsJsonAsync("/api/weekplanner", new { weekStartDate = "2025-03-24" });
        var plan = await planResp.Content.ReadFromJsonAsync<JsonElement>();
        var planId = plan.GetProperty("id").GetString();

        await Client.PostAsJsonAsync($"/api/weekplanner/{planId}/meals", new
            { dayOfWeek = "Monday", mealType = "Dinner", recipeId = r1Id });
        await Client.PostAsJsonAsync($"/api/weekplanner/{planId}/meals", new
            { dayOfWeek = "Tuesday", mealType = "Dinner", recipeId = r2Id });

        var resp = await Client.GetAsync("/api/weekplanner/2025-03-24/shopping-list");
        var list = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();

        // Both onions should be grouped into one entry
        list.Should().HaveCount(1);
        list![0].GetProperty("name").GetString().Should().Be("Onion");
    }

    [Fact]
    public async Task GetShoppingList_ExcludesFreezeMeals()
    {
        // A meal from freezer should not contribute ingredients
        var planResp = await Client.PostAsJsonAsync("/api/weekplanner", new { weekStartDate = "2025-03-31" });
        var plan = await planResp.Content.ReadFromJsonAsync<JsonElement>();
        var planId = plan.GetProperty("id").GetString();

        await Client.PostAsJsonAsync($"/api/weekplanner/{planId}/meals", new
        {
            dayOfWeek = "Wednesday",
            mealType = "Dinner",
            isFromFreezer = true,
            freezerItemName = "Leftover Stew"
        });

        var resp = await Client.GetAsync("/api/weekplanner/2025-03-31/shopping-list");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        list.Should().BeEmpty();
    }
}

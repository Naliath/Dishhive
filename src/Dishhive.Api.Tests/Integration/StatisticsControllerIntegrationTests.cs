using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Dishhive.Api.Tests.Integration;

public class StatisticsControllerIntegrationTests : TestBase
{
    public StatisticsControllerIntegrationTests()
    {
        ClearDatabase();
    }

    // ── Overview ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOverview_ReturnsZeroCountsOnEmptyDatabase()
    {
        var resp = await Client.GetAsync("/api/statistics/overview");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("recipeCount").GetInt32().Should().Be(0);
        doc.GetProperty("familyMemberCount").GetInt32().Should().Be(0);
        doc.GetProperty("weekPlanCount").GetInt32().Should().Be(0);
        doc.GetProperty("plannedMealCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetOverview_ReflectsCreatedData()
    {
        await Client.PostAsJsonAsync("/api/recipes", new
        {
            title = "Spaghetti",
            servings = 4,
            ingredients = Array.Empty<object>(),
            steps = Array.Empty<object>()
        });
        await Client.PostAsJsonAsync("/api/family", new { name = "Alice" });

        var resp = await Client.GetAsync("/api/statistics/overview");
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("recipeCount").GetInt32().Should().Be(1);
        doc.GetProperty("familyMemberCount").GetInt32().Should().Be(1);
    }

    // ── Top Recipes ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTopRecipes_ReturnsEmptyListWhenNoMealsPlanned()
    {
        var resp = await Client.GetAsync("/api/statistics/top-recipes");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        list.Should().BeEmpty();
    }

    // ── Meal Frequency ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetMealFrequency_Returns200()
    {
        var resp = await Client.GetAsync("/api/statistics/meal-frequency");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Recent Weeks ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRecentWeeks_ReturnsEmptyListWhenNoPlans()
    {
        var resp = await Client.GetAsync("/api/statistics/recent-weeks");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        list.Should().BeEmpty();
    }

    // ── Ratings ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRatings_ReturnsEmptyListInitially()
    {
        var resp = await Client.GetAsync("/api/statistics/ratings");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task AddRating_CreatesRating()
    {
        // Create a recipe first
        var recipeResp = await Client.PostAsJsonAsync("/api/recipes", new
        {
            title = "Lasagne",
            servings = 4,
            ingredients = Array.Empty<object>(),
            steps = Array.Empty<object>()
        });
        var recipeDoc = await recipeResp.Content.ReadFromJsonAsync<JsonElement>();
        var recipeId = recipeDoc.GetProperty("id").GetString();

        // Add a rating
        var ratingResp = await Client.PostAsJsonAsync("/api/statistics/ratings", new
        {
            recipeId,
            stars = 5,
            comment = "Absolutely delicious!"
        });
        ratingResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var rating = await ratingResp.Content.ReadFromJsonAsync<JsonElement>();
        rating.GetProperty("stars").GetInt32().Should().Be(5);
        rating.GetProperty("comment").GetString().Should().Be("Absolutely delicious!");
    }

    [Fact]
    public async Task AddRating_WithInvalidStars_ReturnsBadRequest()
    {
        var recipeResp = await Client.PostAsJsonAsync("/api/recipes", new
        {
            title = "Pizza",
            servings = 4,
            ingredients = Array.Empty<object>(),
            steps = Array.Empty<object>()
        });
        var recipeDoc = await recipeResp.Content.ReadFromJsonAsync<JsonElement>();
        var recipeId = recipeDoc.GetProperty("id").GetString();

        var resp = await Client.PostAsJsonAsync("/api/statistics/ratings", new { recipeId, stars = 6 });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddRating_ForNonExistentRecipe_Returns404()
    {
        var resp = await Client.PostAsJsonAsync("/api/statistics/ratings", new
        {
            recipeId = Guid.NewGuid(),
            stars = 3
        });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteRating_RemovesRating()
    {
        var recipeResp = await Client.PostAsJsonAsync("/api/recipes", new
        {
            title = "Soup",
            servings = 2,
            ingredients = Array.Empty<object>(),
            steps = Array.Empty<object>()
        });
        var recipeDoc = await recipeResp.Content.ReadFromJsonAsync<JsonElement>();
        var recipeId = recipeDoc.GetProperty("id").GetString();

        var addResp = await Client.PostAsJsonAsync("/api/statistics/ratings", new { recipeId, stars = 4 });
        var ratingDoc = await addResp.Content.ReadFromJsonAsync<JsonElement>();
        var ratingId = ratingDoc.GetProperty("id").GetString();

        var deleteResp = await Client.DeleteAsync($"/api/statistics/ratings/{ratingId}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify gone
        var listResp = await Client.GetAsync($"/api/statistics/ratings?recipeId={recipeId}");
        var list = await listResp.Content.ReadFromJsonAsync<List<JsonElement>>();
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRatings_FilteredByRecipeId_ReturnsOnlyMatchingRatings()
    {
        // Create two recipes
        var r1Resp = await Client.PostAsJsonAsync("/api/recipes", new
        {
            title = "Pancakes", servings = 2, ingredients = Array.Empty<object>(), steps = Array.Empty<object>()
        });
        var r1Id = (await r1Resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var r2Resp = await Client.PostAsJsonAsync("/api/recipes", new
        {
            title = "Waffles", servings = 2, ingredients = Array.Empty<object>(), steps = Array.Empty<object>()
        });
        var r2Id = (await r2Resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        await Client.PostAsJsonAsync("/api/statistics/ratings", new { recipeId = r1Id, stars = 5 });
        await Client.PostAsJsonAsync("/api/statistics/ratings", new { recipeId = r2Id, stars = 3 });

        var resp = await Client.GetAsync($"/api/statistics/ratings?recipeId={r1Id}");
        var list = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        list.Should().HaveCount(1);
        list![0].GetProperty("stars").GetInt32().Should().Be(5);
    }
}

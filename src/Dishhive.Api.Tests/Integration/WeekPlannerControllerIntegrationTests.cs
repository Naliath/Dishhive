using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;
using Xunit;

namespace Dishhive.Api.Tests.Integration;

public class WeekPlannerControllerIntegrationTests : TestBase
{
    public WeekPlannerControllerIntegrationTests()
    {
        ClearDatabase();
    }

    [Fact]
    public async Task GetByWeek_WhenNoPlanExists_Returns404()
    {
        var response = await Client.GetAsync("/api/weekplanner/2025-01-06");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetOrCreate_CreatesNewPlan()
    {
        var response = await Client.PostAsJsonAsync("/api/weekplanner", new { weekStartDate = "2025-01-06" });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var plan = await response.Content.ReadFromJsonAsync<WeekPlannerDtos.WeekPlanDto>();
        plan.Should().NotBeNull();
        plan!.WeekStartDate.Should().Be(new DateOnly(2025, 1, 6));
        plan.Meals.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrCreate_CalledTwice_ReturnsSamePlan()
    {
        var r1 = await Client.PostAsJsonAsync("/api/weekplanner", new { weekStartDate = "2025-01-13" });
        var plan1 = await r1.Content.ReadFromJsonAsync<WeekPlannerDtos.WeekPlanDto>();

        var r2 = await Client.PostAsJsonAsync("/api/weekplanner", new { weekStartDate = "2025-01-13" });
        var plan2 = await r2.Content.ReadFromJsonAsync<WeekPlannerDtos.WeekPlanDto>();

        plan2!.Id.Should().Be(plan1!.Id);
    }

    [Fact]
    public async Task GetByWeek_AfterCreate_ReturnsPlan()
    {
        await Client.PostAsJsonAsync("/api/weekplanner", new { weekStartDate = "2025-01-20" });

        var response = await Client.GetAsync("/api/weekplanner/2025-01-20");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpsertMeal_AddsMealToPlan()
    {
        var createResp = await Client.PostAsJsonAsync("/api/weekplanner", new { weekStartDate = "2025-01-27" });
        var plan = await createResp.Content.ReadFromJsonAsync<WeekPlannerDtos.WeekPlanDto>();

        var mealResp = await Client.PostAsJsonAsync($"/api/weekplanner/{plan!.Id}/meals", new
        {
            dayOfWeek = "Monday",
            mealType = "Dinner",
            vagueInstruction = "pasta"
        });

        mealResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var meal = await mealResp.Content.ReadFromJsonAsync<WeekPlannerDtos.PlannedMealDto>();
        meal!.VagueInstruction.Should().Be("pasta");
        meal.DayOfWeek.Should().Be("Monday");
    }

    [Fact]
    public async Task UpsertMeal_OverwritesExistingMealForSameSlot()
    {
        var createResp = await Client.PostAsJsonAsync("/api/weekplanner", new { weekStartDate = "2025-02-03" });
        var plan = await createResp.Content.ReadFromJsonAsync<WeekPlannerDtos.WeekPlanDto>();

        await Client.PostAsJsonAsync($"/api/weekplanner/{plan!.Id}/meals", new
        {
            dayOfWeek = "Tuesday", mealType = "Dinner", vagueInstruction = "fish"
        });
        await Client.PostAsJsonAsync($"/api/weekplanner/{plan.Id}/meals", new
        {
            dayOfWeek = "Tuesday", mealType = "Dinner", vagueInstruction = "chicken"
        });

        var planResp = await Client.GetAsync("/api/weekplanner/2025-02-03");
        var updated = await planResp.Content.ReadFromJsonAsync<WeekPlannerDtos.WeekPlanDto>();

        updated!.Meals.Where(m => m.DayOfWeek == "Tuesday" && m.MealType == "Dinner")
            .Should().HaveCount(1)
            .And.ContainSingle(m => m.VagueInstruction == "chicken");
    }

    [Fact]
    public async Task DeleteMeal_RemovesMealFromPlan()
    {
        var createResp = await Client.PostAsJsonAsync("/api/weekplanner", new { weekStartDate = "2025-02-10" });
        var plan = await createResp.Content.ReadFromJsonAsync<WeekPlannerDtos.WeekPlanDto>();

        var mealResp = await Client.PostAsJsonAsync($"/api/weekplanner/{plan!.Id}/meals", new
        {
            dayOfWeek = "Wednesday", mealType = "Dinner", vagueInstruction = "soup"
        });
        var meal = await mealResp.Content.ReadFromJsonAsync<WeekPlannerDtos.PlannedMealDto>();

        var deleteResp = await Client.DeleteAsync($"/api/weekplanner/{plan.Id}/meals/{meal!.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var planResp = await Client.GetAsync("/api/weekplanner/2025-02-10");
        var updated = await planResp.Content.ReadFromJsonAsync<WeekPlannerDtos.WeekPlanDto>();
        updated!.Meals.Should().BeEmpty();
    }

    [Fact]
    public async Task GetShoppingList_WithNoRecipes_ReturnsEmptyList()
    {
        await Client.PostAsJsonAsync("/api/weekplanner", new { weekStartDate = "2025-02-17" });

        var response = await Client.GetAsync("/api/weekplanner/2025-02-17/shopping-list");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<object>>();
        list.Should().BeEmpty();
    }
}

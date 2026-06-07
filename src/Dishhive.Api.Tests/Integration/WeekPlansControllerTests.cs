using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Models.Planning;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

public class WeekPlansControllerTests : TestBase
{
    [Fact]
    public async Task GET_with_weekStart_creates_empty_plan_with_seven_days()
    {
        var monday = new DateOnly(2026, 5, 4); // Monday
        var resp = await Client.GetFromJsonAsync<List<WeekPlanDto>>($"/api/week-plans?weekStart={monday:yyyy-MM-dd}");

        resp.Should().HaveCount(1);
        var plan = resp![0];
        plan.WeekStart.Should().Be(monday);
        plan.Slots.Should().HaveCount(21); // 7 days × 3 meal types
        plan.Slots.Select(s => s.DayOfWeek).Distinct().Should().HaveCount(7);
    }

    [Fact]
    public async Task GET_with_non_monday_normalizes_to_monday()
    {
        var wednesday = new DateOnly(2026, 5, 6);
        var resp = await Client.GetFromJsonAsync<List<WeekPlanDto>>($"/api/week-plans?weekStart={wednesday:yyyy-MM-dd}");

        resp![0].WeekStart.Should().Be(new DateOnly(2026, 5, 4));
    }

    [Fact]
    public async Task Update_slot_with_recipe_and_attendees_persists()
    {
        // Arrange: plan + recipe + family member
        var plan = (await Client.GetFromJsonAsync<List<WeekPlanDto>>("/api/week-plans?weekStart=2026-05-04"))![0];
        var slot = plan.Slots.First();

        var recipe = await (await Client.PostAsJsonAsync("/api/recipes",
            new CreateRecipeDto { Title = "Pasta", Servings = 2 }))
            .Content.ReadFromJsonAsync<RecipeDto>();

        var member = await (await Client.PostAsJsonAsync("/api/family-members",
            new CreateFamilyMemberDto { DisplayName = "Alex" }))
            .Content.ReadFromJsonAsync<FamilyMemberDto>();

        // Act: assign recipe
        var slotResp = await Client.PutAsJsonAsync($"/api/week-plans/{plan.Id}/slots/{slot.Id}",
            new UpdateMealSlotDto { RecipeId = recipe!.Id });
        slotResp.IsSuccessStatusCode.Should().BeTrue();

        // Act: set attendees
        var attResp = await Client.PutAsJsonAsync(
            $"/api/week-plans/{plan.Id}/slots/{slot.Id}/attendees",
            new UpdateAttendeesDto { FamilyMemberIds = { member!.Id } });
        attResp.IsSuccessStatusCode.Should().BeTrue();

        // Assert via reload
        var reloaded = await Client.GetFromJsonAsync<WeekPlanDto>($"/api/week-plans/{plan.Id}");
        var reloadedSlot = reloaded!.Slots.Single(s => s.Id == slot.Id);
        reloadedSlot.RecipeId.Should().Be(recipe.Id);
        reloadedSlot.RecipeTitle.Should().Be("Pasta");
        reloadedSlot.Attendees.Should().HaveCount(1);
        reloadedSlot.Attendees[0].FamilyMemberId.Should().Be(member.Id);
    }

    [Fact]
    public async Task Update_slot_with_vague_intent_clears_recipe()
    {
        var plan = (await Client.GetFromJsonAsync<List<WeekPlanDto>>("/api/week-plans?weekStart=2026-05-11"))![0];
        var slot = plan.Slots.First();

        await Client.PutAsJsonAsync($"/api/week-plans/{plan.Id}/slots/{slot.Id}",
            new UpdateMealSlotDto { VagueIntent = "something with fish", IntentTag = IntentTag.Fish });

        var reloaded = await Client.GetFromJsonAsync<WeekPlanDto>($"/api/week-plans/{plan.Id}");
        var reloadedSlot = reloaded!.Slots.Single(s => s.Id == slot.Id);
        reloadedSlot.RecipeId.Should().BeNull();
        reloadedSlot.VagueIntent.Should().Be("something with fish");
        reloadedSlot.IntentTag.Should().Be(IntentTag.Fish);
    }

    [Fact]
    public async Task Updating_slot_under_wrong_plan_returns_404()
    {
        var resp = await Client.PutAsJsonAsync(
            $"/api/week-plans/{Guid.NewGuid()}/slots/{Guid.NewGuid()}",
            new UpdateMealSlotDto());
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

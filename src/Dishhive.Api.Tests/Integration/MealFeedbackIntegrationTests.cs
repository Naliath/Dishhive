using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Tests.Integration;

/// <summary>
/// Integration tests for the meal eaten/rating feedback endpoints
/// (PUT {id}/eaten, PUT {id}/ratings/{memberId}, DELETE {id}/ratings/{memberId})
/// </summary>
public class MealFeedbackIntegrationTests : TestBase
{
    private static readonly DateOnly Yesterday = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
    private static readonly DateOnly Tomorrow = DateOnly.FromDateTime(DateTime.Today).AddDays(1);

    private async Task<(PlannedMeal Meal, FamilyMember Member)> SeedPastMealWithMemberAsync()
    {
        var member = new FamilyMember { Name = "Naomi" };
        var meal = new PlannedMeal { Date = Yesterday, DishName = "Spaghetti" };
        DbContext.FamilyMembers.Add(member);
        DbContext.PlannedMeals.Add(meal);
        await DbContext.SaveChangesAsync();
        return (meal, member);
    }

    [Fact]
    public async Task SetEaten_PastMeal_ReturnsUpdatedDto()
    {
        var (meal, _) = await SeedPastMealWithMemberAsync();

        var response = await Client.PutAsJsonAsync($"/api/plannedmeals/{meal.Id}/eaten",
            new SetEatenDto { Status = EatenStatus.Eaten });
        var dto = await response.Content.ReadFromJsonAsync<PlannedMealDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        dto!.Eaten.Should().Be(EatenStatus.Eaten);
    }

    [Fact]
    public async Task SetEaten_Null_ClearsTheMark()
    {
        var (meal, _) = await SeedPastMealWithMemberAsync();
        await Client.PutAsJsonAsync($"/api/plannedmeals/{meal.Id}/eaten",
            new SetEatenDto { Status = EatenStatus.Skipped });

        var response = await Client.PutAsJsonAsync($"/api/plannedmeals/{meal.Id}/eaten",
            new SetEatenDto { Status = null });
        var dto = await response.Content.ReadFromJsonAsync<PlannedMealDto>();

        dto!.Eaten.Should().BeNull();
    }

    [Fact]
    public async Task SetEaten_FutureMeal_ReturnsBadRequest()
    {
        var meal = new PlannedMeal { Date = Tomorrow, DishName = "Toekomst" };
        DbContext.PlannedMeals.Add(meal);
        await DbContext.SaveChangesAsync();

        var response = await Client.PutAsJsonAsync($"/api/plannedmeals/{meal.Id}/eaten",
            new SetEatenDto { Status = EatenStatus.Eaten });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetRating_NewRating_IsCreated()
    {
        var (meal, member) = await SeedPastMealWithMemberAsync();

        var response = await Client.PutAsJsonAsync(
            $"/api/plannedmeals/{meal.Id}/ratings/{member.Id}", new SetRatingDto { Rating = 5 });
        var dto = await response.Content.ReadFromJsonAsync<PlannedMealDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        dto!.Ratings.Should().ContainSingle(r => r.FamilyMemberId == member.Id && r.Rating == 5);
    }

    [Fact]
    public async Task SetRating_ExistingRating_IsOverwritten()
    {
        var (meal, member) = await SeedPastMealWithMemberAsync();
        await Client.PutAsJsonAsync(
            $"/api/plannedmeals/{meal.Id}/ratings/{member.Id}", new SetRatingDto { Rating = 2 });

        var response = await Client.PutAsJsonAsync(
            $"/api/plannedmeals/{meal.Id}/ratings/{member.Id}", new SetRatingDto { Rating = 4 });
        var dto = await response.Content.ReadFromJsonAsync<PlannedMealDto>();

        dto!.Ratings.Should().ContainSingle(r => r.FamilyMemberId == member.Id && r.Rating == 4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task SetRating_OutOfRange_ReturnsBadRequest(int rating)
    {
        var (meal, member) = await SeedPastMealWithMemberAsync();

        var response = await Client.PutAsJsonAsync(
            $"/api/plannedmeals/{meal.Id}/ratings/{member.Id}", new SetRatingDto { Rating = rating });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetRating_FutureMeal_ReturnsBadRequest()
    {
        var member = new FamilyMember { Name = "Alex" };
        var meal = new PlannedMeal { Date = Tomorrow, DishName = "Toekomst" };
        DbContext.FamilyMembers.Add(member);
        DbContext.PlannedMeals.Add(meal);
        await DbContext.SaveChangesAsync();

        var response = await Client.PutAsJsonAsync(
            $"/api/plannedmeals/{meal.Id}/ratings/{member.Id}", new SetRatingDto { Rating = 3 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetRating_UnknownMember_ReturnsNotFound()
    {
        var (meal, _) = await SeedPastMealWithMemberAsync();

        var response = await Client.PutAsJsonAsync(
            $"/api/plannedmeals/{meal.Id}/ratings/{Guid.NewGuid()}", new SetRatingDto { Rating = 3 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteRating_RemovesIt()
    {
        var (meal, member) = await SeedPastMealWithMemberAsync();
        await Client.PutAsJsonAsync(
            $"/api/plannedmeals/{meal.Id}/ratings/{member.Id}", new SetRatingDto { Rating = 5 });

        var response = await Client.DeleteAsync($"/api/plannedmeals/{meal.Id}/ratings/{member.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var dto = await Client.GetFromJsonAsync<PlannedMealDto>($"/api/plannedmeals/{meal.Id}");
        dto!.Ratings.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteMeal_CascadesRatings()
    {
        var (meal, member) = await SeedPastMealWithMemberAsync();
        await Client.PutAsJsonAsync(
            $"/api/plannedmeals/{meal.Id}/ratings/{member.Id}", new SetRatingDto { Rating = 5 });

        await Client.DeleteAsync($"/api/plannedmeals/{meal.Id}");

        using var freshContext = CreateFreshContext();
        (await freshContext.MealRatings.CountAsync()).Should().Be(0);
    }
}

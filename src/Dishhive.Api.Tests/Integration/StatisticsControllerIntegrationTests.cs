using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

public class StatisticsControllerIntegrationTests : TestBase
{
    private static readonly DateOnly LastWeek = DateOnly.FromDateTime(DateTime.Today).AddDays(-7);

    [Fact]
    public async Task GetDishStatistics_GroupsByDishName_WithCountAndLastPlanned()
    {
        DbContext.PlannedMeals.AddRange(
            new PlannedMeal { Date = LastWeek, DishName = "Spaghetti" },
            new PlannedMeal { Date = LastWeek.AddDays(2), DishName = "Spaghetti" },
            new PlannedMeal { Date = LastWeek.AddDays(1), DishName = "Vis" });
        await DbContext.SaveChangesAsync();

        var stats = await Client.GetFromJsonAsync<DishStatisticsDto>("/api/statistics/dishes");

        stats!.Dishes.Should().HaveCount(2);
        var spaghetti = stats.Dishes[0];
        spaghetti.DishName.Should().Be("Spaghetti");
        spaghetti.TimesPlanned.Should().Be(2);
        spaghetti.LastPlanned.Should().Be(LastWeek.AddDays(2));
    }

    [Fact]
    public async Task GetDishStatistics_CountsVagueOnlyMealsSeparately()
    {
        DbContext.PlannedMeals.AddRange(
            new PlannedMeal { Date = LastWeek, DishName = "Spaghetti" },
            new PlannedMeal { Date = LastWeek.AddDays(1), VagueInstruction = "iets snel" });
        await DbContext.SaveChangesAsync();

        var stats = await Client.GetFromJsonAsync<DishStatisticsDto>("/api/statistics/dishes");

        stats!.Dishes.Should().ContainSingle();
        stats.UnspecifiedCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDishStatistics_RespectsDateRange()
    {
        DbContext.PlannedMeals.AddRange(
            new PlannedMeal { Date = LastWeek, DishName = "Binnen bereik" },
            new PlannedMeal { Date = LastWeek.AddDays(-30), DishName = "Te oud" });
        await DbContext.SaveChangesAsync();

        var stats = await Client.GetFromJsonAsync<DishStatisticsDto>(
            $"/api/statistics/dishes?from={LastWeek.AddDays(-5):yyyy-MM-dd}");

        stats!.Dishes.Should().ContainSingle(d => d.DishName == "Binnen bereik");
    }

    [Fact]
    public async Task GetMemberStatistics_ReturnsAttendanceAndTopDishes()
    {
        var anna = new FamilyMember { Name = "Anna" };
        DbContext.PlannedMeals.AddRange(
            new PlannedMeal
            {
                Date = LastWeek,
                DishName = "Spaghetti",
                Attendees = { new PlannedMealAttendee { FamilyMember = anna } }
            },
            new PlannedMeal
            {
                Date = LastWeek.AddDays(1),
                DishName = "Spaghetti",
                MealType = MealType.Lunch,
                Attendees = { new PlannedMealAttendee { FamilyMember = anna } }
            },
            // Not attended by Anna
            new PlannedMeal { Date = LastWeek.AddDays(2), DishName = "Vis" });
        await DbContext.SaveChangesAsync();

        var stats = await Client.GetFromJsonAsync<MemberStatisticsDto>($"/api/statistics/members/{anna.Id}");

        stats!.Name.Should().Be("Anna");
        stats.MealsAttended.Should().Be(2);
        stats.TopDishes.Should().ContainSingle(d => d.DishName == "Spaghetti" && d.TimesPlanned == 2);
    }

    [Fact]
    public async Task GetMemberStatistics_UnknownMember_ReturnsNotFound()
    {
        var response = await Client.GetAsync($"/api/statistics/members/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

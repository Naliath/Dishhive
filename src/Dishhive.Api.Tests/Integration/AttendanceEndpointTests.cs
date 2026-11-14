using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dishhive.Api.Tests.Integration;

public sealed class AttendanceEndpointTests : IDisposable
{
    private readonly LocalWorkflowFactory _factory = new();
    private readonly HttpClient _client;
    private readonly FamilyMember _household = new() { Name = "Household" };
    private readonly FamilyMember _second = new() { Name = "Second household" };
    private readonly FamilyMember _guest = new() { Name = "Guest", IsGuest = true };
    private readonly FamilyMember _inactive = new() { Name = "Inactive", IsActive = false };
    private readonly Guid _unknown = Guid.NewGuid();
    private static DateOnly Day => DateOnly.FromDateTime(DateTime.Today).AddDays(14);

    public AttendanceEndpointTests()
    {
        _client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        context.FamilyMembers.AddRange(_household, _second, _guest, _inactive);
        context.SaveChanges();
    }

    public enum Selection { Omitted, Empty, GuestOnly, HouseholdAndGuest, Inactive, Unknown, DuplicateGuest }

    public static IEnumerable<object[]> Matrix() => Enum.GetValues<Selection>()
        .SelectMany(selection => new[] { "create", "update", "generate", "accept" }
            .Select(endpoint => new object[] { endpoint, selection }));

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Attendance_AllEndpointSelections_PreserveDefaultsGuestsAndRejectInvalidIds(string endpoint, Selection selection)
    {
        var requested = selection switch
        {
            Selection.GuestOnly => new[] { _guest.Id },
            Selection.HouseholdAndGuest => [_household.Id, _guest.Id],
            Selection.Inactive => [_household.Id, _inactive.Id],
            Selection.Unknown => [_household.Id, _unknown],
            Selection.DuplicateGuest => [_guest.Id, _guest.Id],
            _ => []
        };
        var expected = requested.Length == 0 ? new[] { _household.Id, _second.Id } : requested.Distinct().ToArray();
        var invalid = selection is Selection.Inactive or Selection.Unknown;
        var payload = new Dictionary<string, object?> { ["date"] = Day, ["dishName"] = "Attendance test dinner" };
        if (selection != Selection.Omitted) payload["familyMemberIds"] = requested;
        Guid? unchangedMeal = null;

        if (endpoint == "generate")
        {
            payload = new() { ["weekStart"] = Day, ["selectedDates"] = new[] { Day } };
            if (selection != Selection.Omitted) payload["attendeeIds"] = requested;
            var response = await _client.PostAsJsonAsync("/api/plannedmeals/suggestions", payload);
            response.StatusCode.Should().Be(invalid ? HttpStatusCode.BadRequest : HttpStatusCode.OK);
            if (invalid)
            {
                _factory.Suggestions.LastRequest.Should().BeNull();
                await AssertMealCount(0);
            }
            else
            {
                var result = (await response.Content.ReadFromJsonAsync<MealSuggestionsDto>())!;
                result.Suggestions.Should().ContainSingle().Which.AttendeeIds.Should().BeEquivalentTo(expected);
                _factory.Suggestions.LastRequest!.Members.Select(member => member.Id).Should().BeEquivalentTo(expected);
            }
            return;
        }

        if (endpoint == "accept")
        {
            var candidate = new MealSuggestionDto
            {
                Id = Guid.NewGuid(), Date = Day, DishName = "Attendance test dinner", AttendeeIds = requested.Distinct().ToList()
            };
            var batch = Guid.NewGuid();
            using (var scope = _factory.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<MealSuggestionAcceptanceService>()
                    .StoreBatchAsync(batch, PlanCompletionStatus.Complete, [candidate]);
            var item = new Dictionary<string, object?> { ["id"] = candidate.Id, ["date"] = Day, ["dishName"] = candidate.DishName };
            if (selection != Selection.Omitted) item["attendeeIds"] = requested;
            var response = await _client.PostAsJsonAsync("/api/plannedmeals/suggestions/accept", new { batchId = batch, suggestions = new[] { item } });
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = (await response.Content.ReadFromJsonAsync<AcceptMealSuggestionsResponseDto>())!.Results.Should().ContainSingle().Subject;
            result.Status.Should().Be(invalid ? "planningFailed" : "created");
            if (invalid) await AssertMealCount(0);
            else await AssertAttendance(result.PlannedMealId!.Value, expected);
            return;
        }

        HttpResponseMessage mutation;
        if (endpoint == "update")
        {
            var original = await _client.PostAsJsonAsync("/api/plannedmeals", new { date = Day, dishName = "Original", familyMemberIds = new[] { _guest.Id } });
            unchangedMeal = (await original.Content.ReadFromJsonAsync<PlannedMealDto>())!.Id;
            mutation = await _client.PutAsJsonAsync($"/api/plannedmeals/{unchangedMeal}", payload);
        }
        else mutation = await _client.PostAsJsonAsync("/api/plannedmeals", payload);
        mutation.StatusCode.Should().Be(invalid ? HttpStatusCode.BadRequest
            : endpoint == "update" ? HttpStatusCode.OK : HttpStatusCode.Created);
        if (invalid)
        {
            await AssertMealCount(unchangedMeal.HasValue ? 1 : 0);
            if (unchangedMeal.HasValue)
            {
                await AssertAttendance(unchangedMeal.Value, [_guest.Id]);
                (await _client.GetFromJsonAsync<PlannedMealDto>($"/api/plannedmeals/{unchangedMeal}"))!.DishName.Should().Be("Original");
            }
        }
        else await AssertAttendance((await mutation.Content.ReadFromJsonAsync<PlannedMealDto>())!.Id, expected);
    }

    [Fact]
    public async Task Acceptance_AttendanceChangedAfterGeneration_RequiresReviewWithoutCreatingMeal()
    {
        var generated = await _client.PostAsJsonAsync("/api/plannedmeals/suggestions", new SuggestWeekRequestDto
            { WeekStart = Day, SelectedDates = [Day], AttendeeIds = [_guest.Id] });
        var batch = (await generated.Content.ReadFromJsonAsync<MealSuggestionsDto>())!;
        var item = batch.Suggestions.Single();
        var accepted = await _client.PostAsJsonAsync("/api/plannedmeals/suggestions/accept", new
        {
            batch.BatchId, suggestions = new[] { new { item.Id, item.Date, item.DishName, attendeeIds = new[] { _household.Id } } }
        });
        (await accepted.Content.ReadFromJsonAsync<AcceptMealSuggestionsResponseDto>())!.Results.Single().Status.Should().Be("needsReview");
        await AssertMealCount(0);
    }

    private async Task AssertAttendance(Guid id, Guid[] expected)
    {
        (await _client.GetFromJsonAsync<PlannedMealDto>($"/api/plannedmeals/{id}"))!.AttendeeIds.Should().BeEquivalentTo(expected);
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        (await context.PlannedMealAttendees.Where(attendee => attendee.PlannedMealId == id)
            .Select(attendee => attendee.FamilyMemberId).ToListAsync()).Should().BeEquivalentTo(expected);
    }

    private async Task AssertMealCount(int expected)
    {
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<DishhiveDbContext>().PlannedMeals.CountAsync()).Should().Be(expected);
    }

    public void Dispose() { _client.Dispose(); _factory.Dispose(); }
}

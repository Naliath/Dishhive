using System.Net.Http.Json;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Dishhive.Api.Tests.Integration;

/// <summary>
/// Integration tests for the meal suggestion endpoints. The Testing environment
/// registers the NoOp service (AI never configured in tests); the enabled path is
/// covered by swapping in a stub via DI.
/// </summary>
public class MealSuggestionsIntegrationTests : TestBase
{
    // The builder excludes days before today, so the fixture must be a week that hasn't
    // started yet (or starts today) regardless of when the suite runs — next Monday,
    // or today itself when today is already a Monday.
    private static readonly DateOnly Monday = NextMondayOnOrAfterToday();

    private static DateOnly NextMondayOnOrAfterToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var offset = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(offset);
    }

    [Fact]
    public async Task SuggestionStatus_WithoutAiConfigured_ReportsDisabled()
    {
        var status = await Client.GetFromJsonAsync<SuggestionStatusDto>("/api/plannedmeals/suggestions/status");

        status!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task SuggestWeek_WithoutAiConfigured_ReturnsDisabledAndEmpty()
    {
        var response = await Client.PostAsJsonAsync("/api/plannedmeals/suggestions",
            new SuggestWeekRequestDto { WeekStart = Monday });
        var dto = await response.Content.ReadFromJsonAsync<MealSuggestionsDto>();

        response.IsSuccessStatusCode.Should().BeTrue();
        dto!.Enabled.Should().BeFalse();
        dto.Suggestions.Should().BeEmpty();
    }

    /// <summary>Stub provider exercising the enabled path through the HTTP pipeline</summary>
    private sealed class StubSuggestionService : IMealSuggestionService
    {
        public MealSuggestionRequest? LastRequest { get; private set; }

        public bool IsEnabled => true;

        public Task<IReadOnlyList<MealSuggestion>> SuggestAsync(
            MealSuggestionRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult<IReadOnlyList<MealSuggestion>>(
            [
                new MealSuggestion { Date = request.WeekStart, DishName = "Stub dish", Reason = "Test" }
            ]);
        }
    }

    private sealed class StubbedFactory(StubSuggestionService stub) : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IMealSuggestionService>(stub));
        }
    }

    [Fact]
    public async Task SuggestWeek_WithEnabledProvider_ReturnsSuggestionsPayload()
    {
        var stub = new StubSuggestionService();
        using var factory = new StubbedFactory(stub);
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<SuggestionStatusDto>("/api/plannedmeals/suggestions/status");
        status!.Enabled.Should().BeTrue();

        var response = await client.PostAsJsonAsync("/api/plannedmeals/suggestions",
            new SuggestWeekRequestDto { WeekStart = Monday });
        var dto = await response.Content.ReadFromJsonAsync<MealSuggestionsDto>();

        dto!.Enabled.Should().BeTrue();
        dto.BatchId.Should().NotBeEmpty();
        dto.Suggestions.Should().ContainSingle();
        dto.Suggestions[0].Id.Should().NotBeEmpty();
        dto.Suggestions[0].DishName.Should().Be("Stub dish");
        dto.Suggestions[0].Date.Should().Be(Monday);

        // The request builder assembled context and asked to fill the whole (empty) week
        stub.LastRequest.Should().NotBeNull();
        stub.LastRequest!.DaysToFill.Should().HaveCount(7);
    }

    [Fact]
    public async Task AcceptSuggestions_RepeatedRequest_IsIdempotent()
    {
        var batchId = Guid.NewGuid();
        var suggestionId = Guid.NewGuid();
        var request = new AcceptMealSuggestionsRequestDto
        {
            BatchId = batchId,
            Suggestions =
            [
                new AcceptMealSuggestionDto
                {
                    Id = suggestionId,
                    Date = Monday,
                    DishName = "Idempotent soup"
                }
            ]
        };

        var first = await Client.PostAsJsonAsync("/api/plannedmeals/suggestions/accept", request);
        var second = await Client.PostAsJsonAsync("/api/plannedmeals/suggestions/accept", request);
        var firstResult = await first.Content.ReadFromJsonAsync<AcceptMealSuggestionsResponseDto>();
        var secondResult = await second.Content.ReadFromJsonAsync<AcceptMealSuggestionsResponseDto>();

        firstResult!.Results.Should().ContainSingle().Which.Status.Should().Be("created");
        secondResult!.Results.Should().ContainSingle().Which.Status.Should().Be("alreadyApplied");
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Data.DishhiveDbContext>();
        context.PlannedMeals.Count(meal => meal.DishName == "Idempotent soup").Should().Be(1);
    }

    [Fact]
    public async Task SuggestWeek_WeekContainingPastDays_ExcludesThemFromDaysToFill()
    {
        var stub = new StubSuggestionService();
        using var factory = new StubbedFactory(stub);
        using var client = factory.CreateClient();

        // A week starting 3 days before today: 3 past days, today, and 3 future days
        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekStart = today.AddDays(-3);

        var response = await client.PostAsJsonAsync("/api/plannedmeals/suggestions",
            new SuggestWeekRequestDto { WeekStart = weekStart });
        response.IsSuccessStatusCode.Should().BeTrue();

        stub.LastRequest!.DaysToFill.Should().HaveCount(4);
        stub.LastRequest!.DaysToFill.Should().OnlyContain(d => d >= today);
        stub.LastRequest!.DaysToFill.Should().NotContain(weekStart);
    }

    [Fact]
    public async Task SuggestWeek_CollectionMentions_AreResolvedIntoConstraints()
    {
        var stub = new StubSuggestionService();
        using var factory = new StubbedFactory(stub);
        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Data.DishhiveDbContext>();
        var recipe = new Models.Recipe { Id = Guid.NewGuid(), Title = "Wrap" };
        context.Recipes.Add(recipe);
        var cookbook = new Models.Cookbook { Id = Guid.NewGuid(), Name = "Easy Weekday Dishes" };
        cookbook.Entries.Add(new Models.CookbookEntry { Cookbook = cookbook, RecipeId = recipe.Id });
        context.Cookbooks.Add(cookbook);
        // Friday carries a day-scoped reference in its vague instruction
        context.PlannedMeals.Add(new Models.PlannedMeal
        {
            Id = Guid.NewGuid(),
            Date = Monday.AddDays(4),
            MealType = Models.MealType.Dinner,
            Course = Models.Course.Main,
            VagueInstruction = "something from #[easy weekday dishes]"
        });
        await context.SaveChangesAsync();

        var response = await client.PostAsJsonAsync("/api/plannedmeals/suggestions",
            new SuggestWeekRequestDto
            {
                WeekStart = Monday,
                Instructions = "prefer #[Easy Weekday Dishes] and ignore #[No Such Collection]"
            });
        response.IsSuccessStatusCode.Should().BeTrue();

        var constraints = stub.LastRequest!.CollectionConstraints;
        var constraint = constraints.Should().ContainSingle().Subject; // the dangling name resolves to nothing
        constraint.Name.Should().Be("Easy Weekday Dishes");
        constraint.RecipeTitles.Should().Equal("Wrap");
        constraint.Dates.Should().Equal(Monday.AddDays(4));
    }
}

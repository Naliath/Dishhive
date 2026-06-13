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
    private static readonly DateOnly Monday = new(2026, 6, 15);

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
        dto.Suggestions.Should().ContainSingle();
        dto.Suggestions[0].DishName.Should().Be("Stub dish");
        dto.Suggestions[0].Date.Should().Be(Monday);

        // The request builder assembled context and asked to fill the whole (empty) week
        stub.LastRequest.Should().NotBeNull();
        stub.LastRequest!.DaysToFill.Should().HaveCount(7);
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

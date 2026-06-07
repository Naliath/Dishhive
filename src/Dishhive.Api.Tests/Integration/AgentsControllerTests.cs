using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

/// <summary>
/// Verifies the AgentsController contract when AI is disabled (the default in test/dev).
/// All write/suggest/chat endpoints must return 503; status returns disabled metadata;
/// learned-sources GET must return an empty list (it does not require AI).
/// </summary>
public class AgentsControllerTests : TestBase
{
    [Fact]
    public async Task Status_returns_disabled_when_ai_provider_is_disabled()
    {
        var resp = await Client.GetAsync("/api/agents/status");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<AgentStatusDto>();
        body.Should().NotBeNull();
        body!.Available.Should().BeFalse();
        body.Provider.Should().Be("disabled");
    }

    [Fact]
    public async Task Suggest_returns_503_when_ai_disabled()
    {
        var resp = await Client.PostAsJsonAsync("/api/agents/meal-planning/suggest", new MealSuggestionRequestDto
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            MealType = "Dinner",
            VagueIntent = "something light",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Chat_returns_503_when_ai_disabled()
    {
        var resp = await Client.PostAsJsonAsync("/api/agents/meal-planning/chat", new ChatRequestDto
        {
            Messages = new() { new ChatTurnDto { Role = "user", Content = "hello" } },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task LearnedSources_returns_empty_list_initially()
    {
        var resp = await Client.GetAsync("/api/agents/learned-sources");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await resp.Content.ReadFromJsonAsync<List<LearnedRecipeSourceDto>>();
        rows.Should().NotBeNull();
        rows!.Should().BeEmpty();
    }
}

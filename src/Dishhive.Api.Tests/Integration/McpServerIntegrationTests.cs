using Dishhive.Api.Models;
using FluentAssertions;
using ModelContextProtocol.Client;

namespace Dishhive.Api.Tests.Integration;

/// <summary>
/// End-to-end MCP protocol tests: the SDK's own client connects to /mcp over the
/// TestServer's HttpClient (streamable HTTP), proving the endpoint routing (incl.
/// the SPA-fallback exclusion), the handshake and real tool calls.
/// </summary>
public class McpServerIntegrationTests : TestBase
{
    private async Task<McpClient> ConnectAsync()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(Client.BaseAddress!, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp
        }, Client, ownsHttpClient: false);
        return await McpClient.CreateAsync(transport);
    }

    [Fact]
    public async Task Mcp_ListTools_ExposesTheFourReadOnlyTools()
    {
        await using var client = await ConnectAsync();

        var tools = await client.ListToolsAsync();

        tools.Select(t => t.Name).Should().BeEquivalentTo(
            "get_week_plan", "get_eaten_on_date", "search_recipes", "get_recipe");
    }

    [Fact]
    public async Task Mcp_SearchRecipes_ReturnsSeededRecipe()
    {
        DbContext.Recipes.Add(new Recipe { Title = "Spaghetti bolognese", Servings = 4 });
        await DbContext.SaveChangesAsync();

        await using var client = await ConnectAsync();
        var result = await client.CallToolAsync("search_recipes",
            new Dictionary<string, object?> { ["query"] = "spaghetti" });

        result.IsError.Should().NotBeTrue();
        var text = string.Join("\n", result.Content
            .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
            .Select(c => c.Text));
        text.Should().Contain("Spaghetti bolognese");
    }

    [Fact]
    public async Task Mcp_GetWeekPlan_ReturnsSeededMeal()
    {
        DbContext.PlannedMeals.Add(new PlannedMeal
        {
            Date = new DateOnly(2026, 6, 17), // a Wednesday
            DishName = "Stoofvlees met frietjes"
        });
        await DbContext.SaveChangesAsync();

        await using var client = await ConnectAsync();
        var result = await client.CallToolAsync("get_week_plan",
            new Dictionary<string, object?> { ["date"] = "2026-06-15" });

        result.IsError.Should().NotBeTrue();
        var text = string.Join("\n", result.Content
            .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
            .Select(c => c.Text));
        text.Should().Contain("Stoofvlees met frietjes");
        text.Should().Contain("2026-06-15"); // normalized weekStart
    }
}

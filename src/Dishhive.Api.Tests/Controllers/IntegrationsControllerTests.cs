using System.Net;
using Dishhive.Api.Controllers;
using Dishhive.Api.Data;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Localization;
using Dishhive.Api.Services.Suggestions;
using Dishhive.Api.Services.WebSearch;
using Dishhive.Api.Tests.Mocks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Dishhive.Api.Tests.Controllers;

public class IntegrationsControllerTests
{
    private const string BaseUrl = "http://searxng:8080/";

    [Fact]
    public async Task GetStatus_JsonSearchForbidden_ReportsActionableMisconfiguration()
    {
        var handler = new MockHttpMessageHandler()
            .FailWith(BaseUrl + "search", HttpStatusCode.Forbidden);

        var status = await GetStatusAsync(handler);

        status.WebSearch.Configured.Should().BeTrue();
        status.WebSearch.Reachable.Should().BeTrue();
        status.WebSearch.Operational.Should().BeFalse();
        status.WebSearch.Error.Should().Contain("JSON search is forbidden");
        status.WebSearch.Error.Should().Contain("search formats");
    }

    [Fact]
    public async Task GetStatus_ValidJsonSearchResponse_ReportsOperational()
    {
        var handler = new MockHttpMessageHandler()
            .RespondWith(BaseUrl + "search", """{"results":[]}""", "application/json");

        var status = await GetStatusAsync(handler);

        status.WebSearch.Reachable.Should().BeTrue();
        status.WebSearch.Operational.Should().BeTrue();
        status.WebSearch.Error.Should().BeNull();
    }

    [Fact]
    public async Task GetStatus_HtmlSearchResponse_ReportsInvalidJsonContract()
    {
        var handler = new MockHttpMessageHandler()
            .RespondWith(BaseUrl + "search", "<html>search</html>");

        var status = await GetStatusAsync(handler);

        status.WebSearch.Reachable.Should().BeTrue();
        status.WebSearch.Operational.Should().BeFalse();
        status.WebSearch.Error.Should().Contain("not valid JSON");
    }

    private static async Task<Dishhive.Api.Models.DTOs.IntegrationStatusResponseDto> GetStatusAsync(
        MockHttpMessageHandler handler)
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler));

        var freezyClient = Substitute.For<IFreezyClient>();
        freezyClient.IsReachableAsync(Arg.Any<CancellationToken>()).Returns(false);

        var scrapersClient = Substitute.For<IRecipeScrapersClient>();
        scrapersClient.GetInstalledVersionAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        await using var context = new DishhiveDbContext(new DbContextOptionsBuilder<DishhiveDbContext>()
            .UseInMemoryDatabase($"Integrations_{Guid.NewGuid()}").Options);
        var controller = new IntegrationsController(httpClientFactory, new UserMessageLocalizer(context));
        return await controller.GetStatus(
            new AiOptions(),
            freezyClient,
            scrapersClient,
            new WebSearchOptions { Provider = "searxng", BaseUrl = BaseUrl },
            new NoOpAiModelCapabilityService(),
            NullLogger<LlmMealSuggestionService>.Instance,
            CancellationToken.None);
    }
}

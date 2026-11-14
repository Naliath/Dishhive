using Dishhive.Api.Services.Ai;
using System.Net;
using Dishhive.Api.Controllers;
using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Localization;
using Dishhive.Api.Services.Suggestions;
using Dishhive.Api.Services.WebSearch;
using Dishhive.Api.Tests.Mocks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
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
    public async Task GetStatus_BlockedSearchEnginesReportsLimitedResultsWhileSourceFallbackRemainsAvailable()
    {
        var handler = new MockHttpMessageHandler().RespondWith(BaseUrl + "search",
            """{"results":[],"unresponsive_engines":[["engine","CAPTCHA"]]}""", "application/json");
        var status = await GetStatusAsync(handler);
        Assert.True(status.WebSearch.Reachable);
        Assert.True(status.WebSearch.Operational);
        Assert.Contains("incomplete", status.WebSearch.Error);
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

    [Fact]
    public async Task GetAiPlanningRuns_SeparatesTaskCallsFromPlanningOutcomes()
    {
        await using var context = new DishhiveDbContext(new DbContextOptionsBuilder<DishhiveDbContext>()
            .UseInMemoryDatabase($"AiStats_{Guid.NewGuid()}").Options);
        var now = DateTime.UtcNow;
        context.AiPlanningRuns.AddRange(
            PlanningRun("success", now, 100, 80, 1),
            PlanningRun("partialFallback", now.AddMinutes(-1), 200, 120, 1),
            PlanningRun("rulesFallback", now.AddMinutes(-2), 300, 0, 0));
        var attempted = TaskRun("recipe.facts", "success", now, 100, 10);
        attempted.OperationAttempt = 3;
        var notSent = TaskRun("recipe.facts", "parseFailure", now.AddSeconds(-1), 200, 20);
        notSent.OperationAttempt = 0;
        context.AiTaskRuns.AddRange(
            attempted, notSent,
            TaskRun("recipe.translate", "validationFailure", now.AddSeconds(-2), 300, 30));
        await context.SaveChangesAsync();

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var controller = new IntegrationsController(httpClientFactory, new UserMessageLocalizer(context));
        var response = await controller.GetAiPlanningRuns(context, new AiReviewMetricsService(context), cancellationToken: CancellationToken.None);

        var metrics = response.Result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeAssignableTo<Dishhive.Api.Models.DTOs.AiPlanningMetricsResponseDto>().Subject;
        metrics.Summary.SuccessfulRuns.Should().Be(1);
        metrics.Summary.PartialFallbackRuns.Should().Be(1);
        metrics.Summary.FallbackRuns.Should().Be(1);
        metrics.Summary.AverageCompletionDurationMs.Should().Be(100);
        metrics.Summary.P95TotalDurationMs.Should().Be(300);
        metrics.TaskMetrics.RunCount.Should().Be(3);
        metrics.Calls.Select(call => call.OperationAttempt).Should().Equal(3, 0, null);
        metrics.TaskMetrics.SuccessfulRuns.Should().Be(1);
        metrics.TaskMetrics.FailedRuns.Should().Be(2);
        metrics.TaskMetrics.P95DurationMs.Should().Be(300);
        metrics.TaskMetrics.Tasks.Single(task => task.Task == "recipe.facts")
            .Should().BeEquivalentTo(new
            {
                RunCount = 2,
                SuccessfulRuns = 1,
                FailedRuns = 1,
                AverageDurationMs = 150d,
                P95DurationMs = 200d,
                TotalTokens = 30L
            });

        var limitedResponse = await controller.GetAiPlanningRuns(
            context, new AiReviewMetricsService(context), count: 2, cancellationToken: CancellationToken.None);
        var limitedMetrics = limitedResponse.Result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeAssignableTo<Dishhive.Api.Models.DTOs.AiPlanningMetricsResponseDto>().Subject;
        limitedMetrics.Calls.Should().HaveCount(2);
        limitedMetrics.TaskMetrics.RunCount.Should().Be(3,
            "summaries cover the retained window rather than only the displayed calls");
    }

    private static AiPlanningRun PlanningRun(
        string outcome, DateTime startedAt, long totalDurationMs,
        long completionDurationMs, int completionAttempts) => new()
    {
        RequestId = Guid.NewGuid().ToString("N")[..12],
        StartedAt = startedAt,
        CompletedAt = startedAt.AddMilliseconds(totalDurationMs),
        Outcome = outcome,
        Provider = "test",
        Model = "model",
        TotalDurationMs = totalDurationMs,
        CompletionDurationMs = completionDurationMs,
        CompletionAttempts = completionAttempts
    };

    private static AiTaskRun TaskRun(
        string task, string outcome, DateTime startedAt, long durationMs, long totalTokens) => new()
    {
        OperationId = Guid.NewGuid().ToString("N")[..12],
        Task = task,
        Provider = "test",
        Model = "model",
        ConfigFingerprint = "config",
        ContractVersion = "v1",
        StartedAt = startedAt,
        CompletedAt = startedAt.AddMilliseconds(durationMs),
        Outcome = outcome,
        DurationMs = durationMs,
        TotalTokens = totalTokens
    };

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
            CancellationToken.None);
    }
}

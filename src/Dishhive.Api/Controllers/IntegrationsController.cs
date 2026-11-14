using Dishhive.Api.Data;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Ai;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Suggestions;
using Dishhive.Api.Services.WebSearch;
using Dishhive.Api.Services.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/integrations")]
public class IntegrationsController(IHttpClientFactory httpClientFactory, UserMessageLocalizer messages) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IntegrationStatusResponseDto> GetStatus(
        [FromServices] AiOptions aiOptions,
        [FromServices] IFreezyClient freezyClient,
        [FromServices] IRecipeScrapersClient scrapersClient,
        [FromServices] WebSearchOptions webSearchOptions,
        [FromServices] IAiModelCapabilityService aiCapability,
        CancellationToken cancellationToken)
    {
        var aiReachable = aiOptions.IsConfigured
            && await CheckAiReachableAsync(aiOptions, cancellationToken);

        var freezyReachable = await freezyClient.IsReachableAsync(cancellationToken);

        var scraperVersion = await scrapersClient.GetInstalledVersionAsync(cancellationToken);

        var webSearchProbe = webSearchOptions.IsConfigured
            ? await CheckWebSearchAsync(webSearchOptions, cancellationToken)
            : WebSearchProbe.NotConfigured;

        return new IntegrationStatusResponseDto(
            Ai: new AiIntegrationStatusDto(
                Configured: aiOptions.IsConfigured,
                Reachable: aiReachable,
                Provider: aiOptions.IsConfigured ? aiOptions.Provider : null,
                Model: aiOptions.IsConfigured ? aiOptions.Model : null,
                BaseUrl: aiOptions.IsConfigured && !string.IsNullOrEmpty(aiOptions.BaseUrl)
                    ? aiOptions.BaseUrl : null,
                StatsEnabled: aiOptions.IsConfigured,
                ModelTestState: StateString(aiCapability.State),
                ModelTestVerdict: aiCapability.Result?.Verdict
            ),
            Freezy: new FreezyIntegrationStatusDto(
                Configured: freezyClient.IsConfigured,
                Reachable: freezyReachable,
                BaseUrl: freezyClient.BaseUrl
            ),
            Scraper: new ScraperIntegrationStatusDto(
                Configured: scrapersClient.IsConfigured,
                Reachable: scraperVersion != null,
                BaseUrl: scrapersClient.BaseUrl,
                PackageVersion: scraperVersion
            ),
            WebSearch: new WebSearchIntegrationStatusDto(
                Configured: webSearchOptions.IsConfigured,
                Reachable: webSearchProbe.Reachable,
                Operational: webSearchProbe.Operational,
                Provider: webSearchOptions.IsConfigured ? webSearchOptions.Provider : null,
                BaseUrl: webSearchOptions.IsConfigured && !string.IsNullOrEmpty(webSearchOptions.BaseUrl)
                    ? webSearchOptions.BaseUrl : null,
                Error: webSearchProbe.Error
            )
        );
    }

    /// <summary>
    /// Probes the same JSON search contract Dishhive uses. A generic health endpoint can
    /// be healthy while SearXNG still rejects format=json because it is not enabled.
    /// </summary>
    private async Task<WebSearchProbe> CheckWebSearchAsync(
        WebSearchOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var baseUrl = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            using var http = httpClientFactory.CreateClient();
            var probeUrl = new Uri(baseUrl, "search?q=dishhive-integration-check&format=json&safesearch=1");
            using var response = await http.GetAsync(probeUrl, cts.Token);

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new WebSearchProbe(
                    Reachable: true,
                    Operational: false,
                    Error: await messages.GetAsync("integration.searchForbidden", cancellationToken));
            }

            if (!response.IsSuccessStatusCode)
            {
                return new WebSearchProbe(
                    Reachable: true,
                    Operational: false,
                    Error: await messages.GetAsync("integration.searchHttpStatus", cancellationToken,
                        ("statusCode", (int)response.StatusCode), ("reason", response.ReasonPhrase)));
            }

            await using var body = await response.Content.ReadAsStreamAsync(cts.Token);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cts.Token);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                return new WebSearchProbe(
                    Reachable: true,
                    Operational: false,
                    Error: await messages.GetAsync("integration.searchUnexpectedResponse", cancellationToken));
            }

            // Source-site fallback remains usable when metasearch engines are blocked.
            var limited = results.GetArrayLength() == 0
                && document.RootElement.TryGetProperty("unresponsive_engines", out var unavailable)
                && unavailable.ValueKind == JsonValueKind.Array && unavailable.GetArrayLength() > 0;
            return new WebSearchProbe(Reachable: true, Operational: true,
                Error: limited ? await messages.GetAsync("integration.searchLimited", cancellationToken) : null);
        }
        catch (JsonException)
        {
            return new WebSearchProbe(
                Reachable: true,
                Operational: false,
                Error: await messages.GetAsync("integration.searchInvalidJson", cancellationToken));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebSearchProbe(
                Reachable: false,
                Operational: false,
                Error: await messages.GetAsync("integration.searchTimeout", cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException)
        {
            return new WebSearchProbe(
                Reachable: false,
                Operational: false,
                Error: await messages.GetAsync("integration.searchUnreachable", cancellationToken));
        }
    }

    private sealed record WebSearchProbe(bool Reachable, bool Operational, string? Error)
    {
        public static readonly WebSearchProbe NotConfigured = new(false, false, null);
    }

    /// <summary>
    /// Checks the recipe-scrapers sidecar for the installed and latest available
    /// package version (the sidecar queries PyPI).
    /// </summary>
    [HttpGet("scraper/version")]
    [ProducesResponseType(typeof(ScraperVersionCheckDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ScraperVersionCheckDto>> GetScraperVersion(
        [FromServices] IRecipeScrapersClient scrapersClient,
        CancellationToken cancellationToken)
    {
        var info = await scrapersClient.GetVersionInfoAsync(cancellationToken);
        if (info == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = await messages.GetAsync("integration.scraperUnavailableTitle", cancellationToken),
                Detail = scrapersClient.IsConfigured
                    ? await messages.GetAsync("integration.scraperUnreachableDetail", cancellationToken)
                    : await messages.GetAsync("integration.scraperNotConfiguredDetail", cancellationToken)
            });
        }

        return new ScraperVersionCheckDto(info.InstalledVersion, info.LatestVersion, info.UpdateAvailable);
    }

    /// <summary>
    /// Updates the recipe-scrapers package in the sidecar (latest version when no
    /// version is given). The sidecar restarts to load the new version, so it is
    /// briefly unreachable afterwards — poll the status endpoint to see it come back.
    /// </summary>
    [HttpPost("scraper/update")]
    [EnableRateLimiting("expensive")]
    [ProducesResponseType(typeof(ScraperUpdateResponseDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ScraperUpdateResponseDto>> UpdateScraper(
        ScraperUpdateRequestDto dto,
        [FromServices] IRecipeScrapersClient scrapersClient,
        CancellationToken cancellationToken)
    {
        var result = await scrapersClient.RequestUpdateAsync(dto.Version, cancellationToken);
        if (!result.Accepted)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Title = await messages.GetAsync("integration.scraperUpdateFailedTitle", cancellationToken),
                Detail = result.Error
            });
        }

        return Accepted(value: new ScraperUpdateResponseDto(result.Version));
    }

    /// <summary>
    /// Latest model capability test for the configured AI model (see AiModelTester):
    /// whether the model produces parseable suggestions at all, which response format
    /// works, and how it scored on the instruction-following evaluation.
    /// </summary>
    [HttpGet("ai/test")]
    [ProducesResponseType(typeof(AiModelTestStatusDto), StatusCodes.Status200OK)]
    public ActionResult<AiModelTestStatusDto> GetAiModelTest(
        [FromServices] IAiModelCapabilityService aiCapability)
    {
        return Ok(ToDto(aiCapability));
    }

    /// <summary>
    /// Starts a fresh model capability test (e.g. after tweaking the model settings or
    /// loading a different model into LM Studio). Runs in the background — poll
    /// GET api/integrations/ai/test until State is "completed". When a test is already
    /// running this joins it (the response reports "running") — a second concurrent
    /// run can never start.
    /// </summary>
    [HttpPost("ai/test")]
    [EnableRateLimiting("expensive")]
    [ProducesResponseType(typeof(AiModelTestStatusDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AiModelTestStatusDto>> RunAiModelTest(
        [FromServices] IAiModelCapabilityService aiCapability,
        [FromServices] AiOptions aiOptions, CancellationToken cancellationToken)
    {
        if (!aiOptions.IsConfigured)
        {
            return BadRequest(new ProblemDetails
            {
                Title = await messages.GetAsync("integration.aiNotConfiguredTitle", cancellationToken),
                Detail = await messages.GetAsync("integration.aiNotConfiguredDetail", cancellationToken)
            });
        }

        // Fire-and-forget: the capability service owns the (never-throwing) run task
        _ = aiCapability.RetestAsync();
        return Accepted(value: ToDto(aiCapability));
    }

    [HttpGet("ai/planning-runs")]
    [ProducesResponseType(typeof(AiPlanningMetricsResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiPlanningMetricsResponseDto>> GetAiPlanningRuns(
        [FromServices] DishhiveDbContext context,
        [FromServices] AiReviewMetricsService reviews,
        [FromQuery] int count = 30,
        [FromQuery] DateTime? from = null,
        [FromQuery] string? task = null,
        [FromQuery] string? model = null,
        [FromQuery] string? fingerprint = null,
        [FromQuery] string? outcome = null,
        [FromQuery] string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        count = Math.Clamp(count, 1, 100);
        var planningQuery = context.AiPlanningRuns.AsNoTracking();
        if (from.HasValue) planningQuery = planningQuery.Where(run => run.StartedAt >= from.Value);
        if (!string.IsNullOrWhiteSpace(model)) planningQuery = planningQuery.Where(run => run.Model == model);
        if (!string.IsNullOrWhiteSpace(fingerprint))
            planningQuery = planningQuery.Where(run => run.ConfigFingerprint == fingerprint);
        if (!string.IsNullOrWhiteSpace(outcome)) planningQuery = planningQuery.Where(run => run.Outcome == outcome);
        if (!string.IsNullOrWhiteSpace(operationId))
            planningQuery = planningQuery.Where(run => run.RequestId == operationId);
        var runs = await planningQuery
            .OrderByDescending(run => run.StartedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
        // Task summaries use the full retained metrics window so a high-volume task
        // cannot hide lower-volume capabilities. The detailed list still honors count.
        var callQuery = context.AiTaskRuns.AsNoTracking();
        if (from.HasValue) callQuery = callQuery.Where(run => run.StartedAt >= from.Value);
        if (!string.IsNullOrWhiteSpace(task)) callQuery = callQuery.Where(run => run.Task == task);
        if (!string.IsNullOrWhiteSpace(model)) callQuery = callQuery.Where(run => run.Model == model);
        if (!string.IsNullOrWhiteSpace(fingerprint))
            callQuery = callQuery.Where(run => run.ConfigFingerprint == fingerprint);
        if (!string.IsNullOrWhiteSpace(outcome)) callQuery = callQuery.Where(run => run.Outcome == outcome);
        if (!string.IsNullOrWhiteSpace(operationId))
            callQuery = callQuery.Where(run => run.OperationId == operationId);
        var metricCalls = await callQuery
            .OrderByDescending(run => run.StartedAt)
            .ToListAsync(cancellationToken);
        var calls = metricCalls.Take(count).ToList();
        var modelRuns = runs.Where(run => run.CompletionAttempts > 0).ToList();
        var summary = new AiPlanningMetricsDto(
            RunCount: runs.Count,
            SuccessfulRuns: runs.Count(run => run.Outcome == "success"),
            PartialFallbackRuns: runs.Count(run => run.Outcome == "partialFallback"),
            FallbackRuns: runs.Count(run => run.Outcome.EndsWith("Fallback", StringComparison.Ordinal)
                && run.Outcome != "partialFallback"),
            AverageTotalDurationMs: runs.Count == 0 ? 0 : runs.Average(run => run.TotalDurationMs),
            AverageCompletionDurationMs: modelRuns.Count == 0 ? 0 : modelRuns.Average(run => run.CompletionDurationMs),
            P95TotalDurationMs: Percentile95(runs.Select(run => run.TotalDurationMs)),
            TotalInputTokens: runs.Sum(run => run.InputTokens),
            TotalOutputTokens: runs.Sum(run => run.OutputTokens),
            TotalReasoningTokens: runs.Sum(run => run.ReasoningTokens),
            TotalSearches: runs.Sum(run => run.SearchCount),
            TotalEmptySearches: runs.Sum(run => run.EmptySearchCount),
            TotalRecipeResolutions: runs.Sum(run => run.RecipeResolutionCount),
            TotalResolutionFailures: runs.Sum(run => run.RecipeResolutionFailureCount),
            TotalParseFailures: runs.Sum(run => run.ParseFailures));

        var successfulOutcomes = new HashSet<string>(StringComparer.Ordinal)
            { "success", "noResult" };
        var cancelledOutcomes = new HashSet<string>(StringComparer.Ordinal) { "cancelled" };
        var incompleteOutcomes = new HashSet<string>(StringComparer.Ordinal)
            { "responseReceived", "abandoned", "completed" };
        var taskSummaries = metricCalls
            .GroupBy(call => new { call.Task, call.Provider, call.Model, call.ConfigFingerprint })
            .OrderBy(group => group.Key.Task)
            .Select(group => new AiTaskSummaryDto(
                group.Key.Task,
                group.Key.Provider,
                group.Key.Model,
                group.Key.ConfigFingerprint,
                group.Count(),
                group.Count(call => successfulOutcomes.Contains(call.Outcome)),
                group.Count(call => !successfulOutcomes.Contains(call.Outcome)
                    && !cancelledOutcomes.Contains(call.Outcome)
                    && !incompleteOutcomes.Contains(call.Outcome)),
                group.Average(call => call.DurationMs),
                Percentile95(group.Select(call => call.DurationMs)),
                group.Sum(call => call.TotalTokens)))
            .ToList();
        var taskMetrics = new AiTaskMetricsDto(
            metricCalls.Count,
            metricCalls.Count(call => successfulOutcomes.Contains(call.Outcome)),
            metricCalls.Count(call => !successfulOutcomes.Contains(call.Outcome)
                && !cancelledOutcomes.Contains(call.Outcome)
                && !incompleteOutcomes.Contains(call.Outcome)),
            metricCalls.Count(call => cancelledOutcomes.Contains(call.Outcome)),
            metricCalls.Count(call => incompleteOutcomes.Contains(call.Outcome)),
            metricCalls.Count == 0 ? 0 : metricCalls.Average(call => call.DurationMs),
            Percentile95(metricCalls.Select(call => call.DurationMs)),
            metricCalls.Sum(call => call.InputTokens),
            metricCalls.Sum(call => call.OutputTokens),
            metricCalls.Sum(call => call.ReasoningTokens),
            taskSummaries);

        return Ok(new AiPlanningMetricsResponseDto(summary, runs.Select(run => new AiPlanningRunDto(
            run.Id, run.RequestId, run.StartedAt, run.Outcome, run.Provider, run.Model,
            run.ConfigFingerprint,
            run.Instructions, run.NormalizedIntentJson, run.Error, run.RequestedDays, run.SuggestedItems,
            run.ExternalSuggestions, run.FallbackSuggestions, run.UsedExternalResearch,
            run.CompletionAttempts, run.ParseFailures, run.ModelTurns, run.InputTokens,
            run.OutputTokens, run.ReasoningTokens, run.TotalTokens, run.ResearchCalls,
            run.SearchCount, run.EmptySearchCount, run.SearchResultCount,
            run.RecipeResolutionCount, run.RecipeResolutionFailureCount,
            run.CapabilityWaitMs, run.CompletionDurationMs, run.SearchDurationMs,
            run.RecipeResolutionDurationMs, run.TotalDurationMs)).ToList(),
            taskMetrics,
            calls.Select(call => new AiTaskRunDto(
                call.Id, call.OperationId, call.Task, call.Provider, call.Model,
                call.ConfigFingerprint, call.ContractVersion, call.StartedAt, call.Outcome,
                call.ResponseMode, call.FinishReason, call.Error, call.Attempt,
                call.MaxOutputTokens, call.Temperature, call.ReasoningDisabled,
                call.SchemaRequested, call.SchemaApplied, call.PromptCharacters,
                call.InputBudgetTokens, call.QueueWaitMs, call.PromptHash,
                call.OutputCharacters, call.InputTokens, call.OutputTokens,
                call.ReasoningTokens, call.TotalTokens, call.DurationMs)
            {
                OperationAttempt = call.OperationAttempt,
                InputFingerprint = call.InputFingerprint,
                InputRevisions = call.InputRevisionsJson == null ? null
                    : System.Text.Json.JsonSerializer.Deserialize<List<AiInputRevisionDto>>(call.InputRevisionsJson,
                        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            }).ToList())
            { Reviews = await reviews.LoadAsync(count, from, task, model, fingerprint, outcome, operationId, cancellationToken) });
    }

    [HttpGet("scraper/update-status")]
    [ProducesResponseType(typeof(ScraperUpdateStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ScraperUpdateStatusDto>> GetScraperUpdateStatus(
        [FromServices] IRecipeScrapersClient scrapersClient,
        CancellationToken cancellationToken)
    {
        var status = await scrapersClient.GetUpdateStatusAsync(cancellationToken);
        if (status is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        return Ok(new ScraperUpdateStatusDto(
            status.Status, status.ActiveVersion, status.UpdatedAt, status.Error));
    }

    private static double Percentile95(IEnumerable<long> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0) return 0;
        var index = Math.Max(0, (int)Math.Ceiling(sorted.Count * 0.95) - 1);
        return sorted[index];
    }

    private static AiModelTestStatusDto ToDto(IAiModelCapabilityService capability)
    {
        var result = capability.Result;
        return new AiModelTestStatusDto(
            State: StateString(capability.State),
            Result: result is null ? null : new AiModelTestResultDto(
                TestedAt: result.TestedAt,
                Verdict: result.Verdict,
                ResponseMode: result.ResponseMode.ToString(),
                EndpointReachable: result.EndpointReachable,
                ModelListed: result.ModelListed,
                FormatCompatible: result.ResponseMode != AiResponseMode.None,
                EvaluationPassed: result.EvaluationPassed,
                PlanningQualityPassed: result.PlanningIntentPassed,
                TokensPerSecond: result.TokensPerSecond,
                ElapsedMs: result.ElapsedMs,
                Checks: [.. result.Checks.Select(c => new AiModelTestCheckDto(c.Name, c.Passed, c.Detail))])
                { PlanningEligible = result.PlanningEligible });
    }

    private static string StateString(AiModelTestState state) => state switch
    {
        AiModelTestState.NotConfigured => "notConfigured",
        AiModelTestState.NotRun => "notRun",
        AiModelTestState.Running => "running",
        _ => "completed"
    };

    private async Task<bool> CheckAiReachableAsync(AiOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var baseUrl = ChatClientFactory.ProbeBaseUrl(options);
            if (baseUrl is null) return false;

            using var http = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, "models"));

            var apiKey = options.ResolveApiKey();
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await http.SendAsync(request, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

}

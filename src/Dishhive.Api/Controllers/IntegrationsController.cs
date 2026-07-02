using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Suggestions;
using Dishhive.Api.Services.WebSearch;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/integrations")]
public class IntegrationsController(IHttpClientFactory httpClientFactory) : ControllerBase
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

        var webSearchReachable = webSearchOptions.IsConfigured
            && await CheckWebSearchReachableAsync(webSearchOptions, cancellationToken);

        return new IntegrationStatusResponseDto(
            Ai: new AiIntegrationStatusDto(
                Configured: aiOptions.IsConfigured,
                Reachable: aiReachable,
                Provider: aiOptions.IsConfigured ? aiOptions.Provider : null,
                Model: aiOptions.IsConfigured ? aiOptions.Model : null,
                BaseUrl: aiOptions.IsConfigured && !string.IsNullOrEmpty(aiOptions.BaseUrl)
                    ? aiOptions.BaseUrl : null,
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
                Reachable: webSearchReachable,
                Provider: webSearchOptions.IsConfigured ? webSearchOptions.Provider : null,
                BaseUrl: webSearchOptions.IsConfigured && !string.IsNullOrEmpty(webSearchOptions.BaseUrl)
                    ? webSearchOptions.BaseUrl : null
            )
        );
    }

    /// <summary>Probes the SearXNG instance's /healthz endpoint (returns "OK" when up)</summary>
    private async Task<bool> CheckWebSearchReachableAsync(WebSearchOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var baseUrl = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            using var http = httpClientFactory.CreateClient();
            using var response = await http.GetAsync(new Uri(baseUrl, "healthz"), cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
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
                Title = "Scraper service unavailable",
                Detail = scrapersClient.IsConfigured
                    ? "The recipe scraper service could not be reached."
                    : "The recipe scraper service is not configured (RecipeScrapers__BaseUrl)."
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
                Title = "Scraper update failed",
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
    [ProducesResponseType(typeof(AiModelTestStatusDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<AiModelTestStatusDto> RunAiModelTest(
        [FromServices] IAiModelCapabilityService aiCapability,
        [FromServices] AiOptions aiOptions)
    {
        if (!aiOptions.IsConfigured)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "AI is not configured",
                Detail = "Set Ai__Provider and Ai__Model before testing a model."
            });
        }

        // Fire-and-forget: the capability service owns the (never-throwing) run task
        _ = aiCapability.RetestAsync();
        return Accepted(value: ToDto(aiCapability));
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
                EvaluationPassed: result.EvaluationPassed,
                TokensPerSecond: result.TokensPerSecond,
                ElapsedMs: result.ElapsedMs,
                Checks: [.. result.Checks.Select(c => new AiModelTestCheckDto(c.Name, c.Passed, c.Detail))]));
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

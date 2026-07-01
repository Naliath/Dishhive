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
                    ? aiOptions.BaseUrl : null
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

    private async Task<bool> CheckAiReachableAsync(AiOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var baseUrl = ResolveBaseUrl(options);
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

    /// <summary>
    /// Resolves the base URL for the /models health probe. Explicit BaseUrl wins,
    /// then the per-provider SDK default, then known cloud-provider fallbacks.
    /// </summary>
    private static Uri? ResolveBaseUrl(AiOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            return new Uri(options.BaseUrl.TrimEnd('/') + "/");

        var defaultEndpoint = ChatClientFactory.DefaultEndpoint(options.NormalizedProvider);
        if (defaultEndpoint is not null)
            return new Uri(defaultEndpoint.ToString().TrimEnd('/') + "/");

        return options.NormalizedProvider switch
        {
            "openai" => new Uri("https://api.openai.com/v1/"),
            _ => null
        };
    }
}

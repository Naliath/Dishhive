using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Suggestions;
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
        CancellationToken cancellationToken)
    {
        var aiReachable = aiOptions.IsConfigured
            && await CheckAiReachableAsync(aiOptions, cancellationToken);

        var freezyReachable = await freezyClient.IsReachableAsync(cancellationToken);

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
            )
        );
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
                if (options.NormalizedProvider == "anthropic")
                {
                    request.Headers.Add("x-api-key", apiKey);
                    request.Headers.Add("anthropic-version", "2023-06-01");
                }
                else
                {
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", apiKey);
                }
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
            "anthropic" => new Uri("https://api.anthropic.com/v1/"),
            _ => null
        };
    }
}

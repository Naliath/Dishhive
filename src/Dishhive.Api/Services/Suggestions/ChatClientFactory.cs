using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>
/// Builds a Microsoft.Extensions.AI IChatClient from configuration.
/// All supported providers (OpenAI, Mistral, Ollama, LM Studio, openai-compatible)
/// speak the OpenAI-compatible chat API and share the OpenAI SDK with a per-provider endpoint.
/// </summary>
public static class ChatClientFactory
{
    public static IChatClient Create(AiOptions options)
    {
        if (!options.IsConfigured)
        {
            throw new InvalidOperationException("AI is not configured; check Ai:Provider, Ai:Model and Ai:ApiKey.");
        }

        var provider = options.NormalizedProvider;
        return provider switch
        {
            "openai" or "mistral" or "ollama" or "lmstudio" or "openai-compatible"
                => CreateOpenAiCompatible(options),
            _ => throw new InvalidOperationException(
                $"Unknown AI provider '{options.Provider}'. " +
                "Supported (all OpenAI-compatible): openai, mistral, ollama, lmstudio, openai-compatible.")
        };
    }

    /// <summary>Default endpoint per OpenAI-compatible provider; Ai:BaseUrl overrides</summary>
    public static Uri? DefaultEndpoint(string provider) => provider switch
    {
        "mistral" => new Uri("https://api.mistral.ai/v1"),
        "ollama" => new Uri("http://localhost:11434/v1"),
        "lmstudio" => new Uri("http://localhost:1234/v1"),
        _ => null // openai uses the SDK default; openai-compatible requires BaseUrl
    };

    /// <summary>
    /// Base URL (with trailing slash) for the /models health/capability probes.
    /// Explicit BaseUrl wins, then the per-provider default, then known cloud endpoints
    /// the SDK defaults to. Null when no probe target can be determined.
    /// </summary>
    public static Uri? ProbeBaseUrl(AiOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return new Uri(options.BaseUrl.TrimEnd('/') + "/");
        }

        var defaultEndpoint = DefaultEndpoint(options.NormalizedProvider);
        if (defaultEndpoint is not null)
        {
            return new Uri(defaultEndpoint.ToString().TrimEnd('/') + "/");
        }

        return options.NormalizedProvider switch
        {
            "openai" => new Uri("https://api.openai.com/v1/"),
            _ => null
        };
    }

    private static IChatClient CreateOpenAiCompatible(AiOptions options)
    {
        var endpoint = !string.IsNullOrWhiteSpace(options.BaseUrl)
            ? new Uri(options.BaseUrl.TrimEnd('/'))
            : DefaultEndpoint(options.NormalizedProvider);

        // The IChatClient is a singleton shared by both the plain-suggestion path
        // (TimeoutSeconds) and the agentic external-recipe path (AgentTimeoutSeconds,
        // several full model round-trips). The SDK's own per-call network timeout must
        // cover whichever is larger, or a single slow turn on the agentic path gets
        // killed here well before the caller's own (correct, longer) budget expires —
        // the actual per-path deadline is already enforced by the linked
        // CancellationTokenSource in LlmMealSuggestionService.
        var networkTimeout = TimeSpan.FromSeconds(Math.Max(options.TimeoutSeconds, options.AgentTimeoutSeconds));
        var clientOptions = new OpenAIClientOptions
        {
            NetworkTimeout = networkTimeout,
            // The SDK's default retry policy (3 attempts, exponential backoff) can burn
            // most of that per-call timeout retrying a single doomed request before our
            // own cancellation budget or corrective-reprompt logic ever gets a turn.
            // One retry is enough for a genuine transient blip; a dead endpoint should
            // fail in seconds, not minutes.
            RetryPolicy = new ClientRetryPolicy(maxRetries: 1)
        };
        if (endpoint != null)
        {
            clientOptions.Endpoint = endpoint;
        }

        // Local providers ignore the key but the SDK requires a non-empty value
        var apiKey = options.ResolveApiKey() ?? "not-needed";

        return new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions)
            .GetChatClient(options.Model)
            .AsIChatClient();
    }
}

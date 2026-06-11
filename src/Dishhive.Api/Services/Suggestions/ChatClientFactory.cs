using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>
/// Builds a Microsoft.Extensions.AI IChatClient from configuration.
///
/// Four of the five supported providers (OpenAI, Mistral, Ollama, LM Studio) speak the
/// OpenAI-compatible chat API and share the OpenAI SDK with a per-provider endpoint;
/// Anthropic uses its official SDK, which implements IChatClient directly.
/// See docs/features/ai-week-planning.md for the research behind this choice.
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
            "anthropic" => CreateAnthropic(options),
            "openai" or "mistral" or "ollama" or "lmstudio" or "openai-compatible"
                => CreateOpenAiCompatible(options),
            _ => throw new InvalidOperationException(
                $"Unknown AI provider '{options.Provider}'. " +
                "Supported: openai, anthropic, mistral, ollama, lmstudio, openai-compatible.")
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

    private static IChatClient CreateOpenAiCompatible(AiOptions options)
    {
        var endpoint = !string.IsNullOrWhiteSpace(options.BaseUrl)
            ? new Uri(options.BaseUrl.TrimEnd('/'))
            : DefaultEndpoint(options.NormalizedProvider);

        var clientOptions = new OpenAIClientOptions
        {
            NetworkTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
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

    private static IChatClient CreateAnthropic(AiOptions options)
    {
        IAnthropicClient client = new AnthropicClient
        {
            ApiKey = options.ResolveApiKey(),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            client = client.WithOptions(o => o with { BaseUrl = options.BaseUrl.TrimEnd('/') });
        }

        return client.AsIChatClient(options.Model, options.MaxOutputTokens);
    }
}

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Dishhive.Api.Services.Agents;

/// <summary>
/// Builds <see cref="IChatClient"/> instances based on <see cref="AiAgentOptions.Provider"/>.
/// Returns <c>null</c> when AI is disabled, so consumers can register a single
/// <c>IChatClient?</c> in DI and short-circuit cleanly.
///
/// Tests inject their own <c>IChatClient</c> by replacing this factory in <c>ConfigureTestServices</c>.
/// </summary>
public interface IChatClientFactory
{
    /// <summary>Returns a singleton chat client, or <c>null</c> when AI is disabled.</summary>
    IChatClient? Get();

    /// <summary>Whether the configured provider is non-<c>disabled</c>.</summary>
    bool IsAvailable { get; }

    /// <summary>Configured model name (for diagnostics).</summary>
    string Model { get; }

    /// <summary>Configured provider name (for diagnostics).</summary>
    string Provider { get; }
}

public sealed class ChatClientFactory : IChatClientFactory, IDisposable
{
    private readonly AiAgentOptions _options;
    private readonly Lazy<IChatClient?> _client;

    public ChatClientFactory(IOptions<AiAgentOptions> options)
    {
        _options = options.Value;
        _client = new Lazy<IChatClient?>(Create);
    }

    public bool IsAvailable => _options.IsEnabled;
    public string Model => _options.Model;
    public string Provider => _options.Provider;

    public IChatClient? Get() => _client.Value;

    private IChatClient? Create()
    {
        if (!_options.IsEnabled) return null;

        return _options.Provider.ToLowerInvariant() switch
        {
            "openai" => CreateOpenAI(),
            // "ollama" intentionally omitted — adding it is one DI line via Microsoft.Extensions.AI.Ollama.
            _ => throw new AgentUnavailableException(
                $"Unsupported AI provider '{_options.Provider}'. Set Dishhive:Ai:Provider to 'openai' or 'disabled'."),
        };
    }

    private IChatClient CreateOpenAI()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new AgentUnavailableException("Dishhive:Ai:ApiKey is required when Provider is 'openai'.");

        var openAi = string.IsNullOrWhiteSpace(_options.Endpoint)
            ? new OpenAIClient(_options.ApiKey)
            : new OpenAIClient(
                new System.ClientModel.ApiKeyCredential(_options.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(_options.Endpoint) });

        return openAi.GetChatClient(_options.Model)
                     .AsIChatClient()
                     .AsBuilder()
                     .UseFunctionInvocation()
                     .Build();
    }

    public void Dispose()
    {
        if (_client.IsValueCreated && _client.Value is IDisposable d) d.Dispose();
    }
}

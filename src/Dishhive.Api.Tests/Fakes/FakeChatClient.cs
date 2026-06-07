using Microsoft.Extensions.AI;

namespace Dishhive.Api.Tests.Fakes;

/// <summary>
/// Deterministic <see cref="IChatClient"/> stand-in. Each call returns the next response
/// from the queued list (last response repeats once exhausted). Records every call for
/// invocation-count assertions.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly Queue<string> _responses;
    private readonly string _fallback;

    public int CallCount { get; private set; }
    public List<IList<ChatMessage>> Calls { get; } = new();

    public FakeChatClient(IEnumerable<string> responses)
    {
        _responses = new Queue<string>(responses);
        _fallback = _responses.LastOrDefault() ?? "{}";
    }

    public FakeChatClient(string response) : this(new[] { response }) { }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        Calls.Add(messages.ToList());
        var text = _responses.Count > 0 ? _responses.Dequeue() : _fallback;
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var resp = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var msg in resp.Messages)
        {
            foreach (var content in msg.Contents)
            {
                yield return new ChatResponseUpdate { Role = msg.Role, Contents = new List<AIContent> { content } };
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>
/// Test double for <see cref="Dishhive.Api.Services.Agents.IChatClientFactory"/> that
/// always returns the supplied <see cref="FakeChatClient"/>.
/// </summary>
public sealed class FakeChatClientFactory : Dishhive.Api.Services.Agents.IChatClientFactory
{
    private readonly FakeChatClient _client;

    public FakeChatClientFactory(FakeChatClient client)
    {
        _client = client;
    }

    public IChatClient? Get() => _client;
    public bool IsAvailable => true;
    public string Model => "fake";
    public string Provider => "fake";
}

namespace Dishhive.Api.Services.Agents;

/// <summary>
/// Thrown when an agent endpoint is invoked while AI is disabled or the provider
/// is unreachable. The controller surface translates this to <c>503 Service Unavailable</c>.
/// </summary>
public sealed class AgentUnavailableException : Exception
{
    public AgentUnavailableException(string message) : base(message) { }
    public AgentUnavailableException(string message, Exception inner) : base(message, inner) { }
}

namespace Dishhive.Api.Services.Agents;

/// <summary>
/// IOptions-bound configuration for AI agent integration. See <c>docs/features/ai-agents.md</c>.
/// </summary>
public sealed class AiAgentOptions
{
    public const string SectionName = "Dishhive:Ai";

    /// <summary><c>openai</c> | <c>ollama</c> | <c>disabled</c> (default).</summary>
    public string Provider { get; set; } = "disabled";

    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Optional override (Ollama / Azure OpenAI gateway / local proxy).</summary>
    public string? Endpoint { get; set; }

    /// <summary>API key. Read from <c>DISHHIVE__AI__APIKEY</c> in deployment.</summary>
    public string? ApiKey { get; set; }

    public int RequestTimeoutSeconds { get; set; } = 60;

    public RecipeImportAiOptions RecipeImport { get; set; } = new();

    public bool IsEnabled => !string.Equals(Provider, "disabled", StringComparison.OrdinalIgnoreCase);
}

public sealed class RecipeImportAiOptions
{
    /// <summary>HTML is truncated to this many chars before being sent to the LLM.</summary>
    public int MaxHtmlChars { get; set; } = 60_000;
}

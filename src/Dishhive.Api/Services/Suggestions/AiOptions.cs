namespace Dishhive.Api.Services.Suggestions;

/// <summary>
/// Configuration for the optional AI week-plan suggestions ("Ai" section,
/// Ai__* env vars in Docker). Disabled when no provider is configured —
/// the NoOp suggestion service stays registered (Freezy pattern).
/// See docs/features/ai-week-planning.md.
/// </summary>
public class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// openai | anthropic | mistral | ollama | lmstudio | openai-compatible
    /// </summary>
    public string Provider { get; set; } = "";

    /// <summary>
    /// Falls back to the industry-standard OPENAI_API_KEY / ANTHROPIC_API_KEY
    /// env vars when empty; local providers (ollama, lmstudio) need no key
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Optional endpoint override; per-provider defaults apply when empty
    /// (required for openai-compatible)
    /// </summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Model name, e.g. llama3.1, gpt-4o-mini, claude-opus-4-8</summary>
    public string Model { get; set; } = "";

    /// <summary>
    /// Generous default: reasoning models (Qwen3, DeepSeek-R1, ...) spend their
    /// thinking inside the output budget before any JSON appears — instruction-heavy
    /// requests routinely burn 6-8k thinking tokens, so leave ample headroom
    /// (pairs with a ≥16k model context window)
    /// </summary>
    public int MaxOutputTokens { get; set; } = 12000;

    /// <summary>Timeout for one suggestion call; local models can be slow</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Prepends the /no_think soft switch to the prompt. Local reasoning models
    /// (Qwen3 family and friends) otherwise spend the whole output window thinking
    /// and never emit the JSON; other models ignore the token. Default on.
    /// </summary>
    public bool DisableThinking { get; set; } = true;

    /// <summary>Providers that run locally and need no API key</summary>
    private static readonly string[] LocalProviders = ["ollama", "lmstudio", "openai-compatible"];

    public string NormalizedProvider => Provider.Trim().ToLowerInvariant();

    /// <summary>
    /// Resolves the API key: explicit Ai:ApiKey wins, then the provider's
    /// standard environment variable (OPENAI_API_KEY, ANTHROPIC_API_KEY, MISTRAL_API_KEY)
    /// </summary>
    public string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            return ApiKey.Trim();
        }

        var envVar = NormalizedProvider switch
        {
            "openai" => "OPENAI_API_KEY",
            "anthropic" => "ANTHROPIC_API_KEY",
            "mistral" => "MISTRAL_API_KEY",
            _ => null
        };

        return envVar != null ? Environment.GetEnvironmentVariable(envVar) : null;
    }

    /// <summary>
    /// Configured when a provider and model are set, and cloud providers also
    /// have an API key. openai-compatible additionally requires a BaseUrl.
    /// </summary>
    public bool IsConfigured
    {
        get
        {
            var provider = NormalizedProvider;
            if (provider.Length == 0 || string.IsNullOrWhiteSpace(Model))
            {
                return false;
            }

            if (provider == "openai-compatible" && string.IsNullOrWhiteSpace(BaseUrl))
            {
                return false;
            }

            return LocalProviders.Contains(provider)
                || !string.IsNullOrWhiteSpace(ResolveApiKey());
        }
    }
}

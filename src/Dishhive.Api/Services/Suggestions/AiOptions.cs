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
    /// openai | mistral | ollama | lmstudio | openai-compatible (all OpenAI-compatible)
    /// </summary>
    public string Provider { get; set; } = "";

    /// <summary>
    /// Falls back to the industry-standard OPENAI_API_KEY / MISTRAL_API_KEY
    /// env vars when empty; local providers (ollama, lmstudio) need no key
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Optional endpoint override; per-provider defaults apply when empty
    /// (required for openai-compatible)
    /// </summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Model name, e.g. llama3.1, gpt-4o-mini, mistral-small-latest</summary>
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
    /// Timeout for a suggestion call that uses the external-recipe tools (web search +
    /// scraping). Much longer than <see cref="TimeoutSeconds"/>: a tool loop makes
    /// several round-trips — each one a full model completion — and both local models
    /// and cloud APIs under load can take tens of seconds per turn. Applies only on the
    /// agentic path.
    /// </summary>
    public int AgentTimeoutSeconds { get; set; } = 300;

    /// <summary>Upper bound on tool-call iterations per agentic suggestion request</summary>
    public int MaxToolIterations { get; set; } = 8;

    /// <summary>
    /// Prepends the /no_think soft switch to the prompt. Local reasoning models
    /// (Qwen3 family and friends) otherwise spend the whole output window thinking
    /// and never emit the JSON; other models ignore the token. Default on.
    /// </summary>
    public bool DisableThinking { get; set; } = true;

    /// <summary>
    /// Sampling temperature for the suggestion call. Low by default: this is a
    /// structured planning task, so steadier, less "creative" output parses more
    /// reliably and makes regeneration less random.
    /// </summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// Extra reprompt attempts when the first reply can't be parsed (a corrective
    /// "reply with ONLY JSON" turn is appended before retrying). 0 disables retries;
    /// every attempt still shares the single <see cref="TimeoutSeconds"/> budget.
    /// </summary>
    public int MaxRetries { get; set; } = 1;

    /// <summary>
    /// Rough token budget for the variable-length prompt blocks (recipe list +
    /// history). The builder sizes those blocks to fit instead of using fixed
    /// caps, so the prompt scales with the model's context window. Estimated at
    /// ~4 chars/token; the actual usage is logged for tuning.
    /// </summary>
    public int MaxPromptTokens { get; set; } = 6000;

    /// <summary>
    /// Fingerprint of every setting that affects the model capability test's outcome —
    /// the persisted test result is keyed by this, so changing any of these settings
    /// triggers a fresh test at the next startup. Operational knobs (timeouts, retries)
    /// are deliberately excluded: tuning them doesn't change what the model can do.
    /// </summary>
    public string CapabilityFingerprint => string.Join("|",
        NormalizedProvider,
        Model.Trim(),
        BaseUrl.Trim().TrimEnd('/'),
        MaxOutputTokens,
        Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture),
        MaxPromptTokens,
        DisableThinking);

    /// <summary>Providers that run locally and need no API key</summary>
    private static readonly string[] LocalProviders = ["ollama", "lmstudio", "openai-compatible"];

    public string NormalizedProvider => Provider.Trim().ToLowerInvariant();

    /// <summary>
    /// Resolves the API key: explicit Ai:ApiKey wins, then the provider's
    /// standard environment variable (OPENAI_API_KEY, MISTRAL_API_KEY)
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

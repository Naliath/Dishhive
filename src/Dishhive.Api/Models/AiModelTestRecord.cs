namespace Dishhive.Api.Models;

/// <summary>
/// Persisted outcome of the AI model capability test (see AiModelTester), one row per
/// AI configuration fingerprint. Lets a restart reuse the verdict instead of re-testing
/// the model on every boot — a fresh test only runs when the fingerprint has no row yet
/// (i.e. the AI settings changed) or the user re-triggers it from the settings page.
/// </summary>
public class AiModelTestRecord
{
    public Guid Id { get; set; }

    /// <summary>Fingerprint of every AI setting that affects the test outcome
    /// (see AiOptions.CapabilityFingerprint); unique — upserted per configuration</summary>
    public string ConfigKey { get; set; } = "";

    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTimeOffset TestedAt { get; set; }
    public bool EndpointReachable { get; set; }
    public bool? ModelListed { get; set; }

    /// <summary>AiResponseMode enum name (None | JsonSchema | PromptedJson)</summary>
    public string ResponseMode { get; set; } = "";

    public bool? EvaluationPassed { get; set; }

    /// <summary>The per-check details as a JSON array of AiModelTestCheck</summary>
    public string ChecksJson { get; set; } = "[]";

    public double? TokensPerSecond { get; set; }
    public long ElapsedMs { get; set; }
}

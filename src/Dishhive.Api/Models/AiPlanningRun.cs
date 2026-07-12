using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>Durable technical metrics for one AI week-planning request.</summary>
public class AiPlanningRun
{
    public Guid Id { get; set; }

    [MaxLength(16)]
    public string RequestId { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }

    [MaxLength(30)]
    public string Outcome { get; set; } = "running";

    [MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Model { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Instructions { get; set; }

    /// <summary>Versioned, language-neutral constraints interpreted for this run.</summary>
    public string? NormalizedIntentJson { get; set; }

    [MaxLength(1000)]
    public string? Error { get; set; }

    public int RequestedDays { get; set; }
    public int SuggestedItems { get; set; }
    public int ExternalSuggestions { get; set; }
    public int FallbackSuggestions { get; set; }
    public bool UsedExternalResearch { get; set; }
    public int CompletionAttempts { get; set; }
    public int ParseFailures { get; set; }
    public int ModelTurns { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long ReasoningTokens { get; set; }
    public long TotalTokens { get; set; }
    public int ResearchCalls { get; set; }
    public int SearchCount { get; set; }
    public int EmptySearchCount { get; set; }
    public int SearchResultCount { get; set; }
    public int RecipeResolutionCount { get; set; }
    public int RecipeResolutionFailureCount { get; set; }
    public long CapabilityWaitMs { get; set; }
    public long CompletionDurationMs { get; set; }
    public long SearchDurationMs { get; set; }
    public long RecipeResolutionDurationMs { get; set; }
    public long TotalDurationMs { get; set; }
}

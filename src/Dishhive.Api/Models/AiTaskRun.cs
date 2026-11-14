using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>Durable metrics for one model completion, regardless of product feature.</summary>
public class AiTaskRun
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string OperationId { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Task { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Model { get; set; } = string.Empty;

    [MaxLength(500)]
    public string ConfigFingerprint { get; set; } = string.Empty;

    [MaxLength(80)]
    public string ContractVersion { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }

    [MaxLength(30)]
    public string Outcome { get; set; } = "completed";

    [MaxLength(30)]
    public string ResponseMode { get; set; } = "promptedJson";

    [MaxLength(40)]
    public string? FinishReason { get; set; }

    [MaxLength(1000)]
    public string? Error { get; set; }

    public int Attempt { get; set; }
    /// <summary>Physical provider attempt within its operation; 0 = no call, null = legacy unknown.</summary>
    public int? OperationAttempt { get; set; }
    public int MaxOutputTokens { get; set; }
    public double Temperature { get; set; }
    public bool ReasoningDisabled { get; set; }
    public bool SchemaRequested { get; set; }
    public bool SchemaApplied { get; set; }
    public long PromptCharacters { get; set; }
    public int InputBudgetTokens { get; set; }
    public long QueueWaitMs { get; set; }
    [MaxLength(64)]
    public string PromptHash { get; set; } = string.Empty;
    /// <summary>Source versions/hashes for the enclosing operation; null means not recorded.</summary>
    public string? InputRevisionsJson { get; set; }
    [MaxLength(64)] public string? InputFingerprint { get; set; }
    public long OutputCharacters { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long ReasoningTokens { get; set; }
    public long TotalTokens { get; set; }
    public long DurationMs { get; set; }
}

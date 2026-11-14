using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

public enum RecipeEnrichmentTask
{
    Facts = 0,
    Translation = 1
}

public enum RecipeEnrichmentJobState
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Superseded = 3,
    Failed = 4,
    Cancelled = 5
}

/// <summary>
/// Durable, retryable enrichment work. Jobs operate on a captured recipe revision;
/// results are applied only when the source and human-confirmation versions match.
/// </summary>
public class RecipeEnrichmentJob
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public Recipe? Recipe { get; set; }
    public long RecipeRevision { get; set; }
    public long FactsConfirmationVersion { get; set; }
    public RecipeEnrichmentTask Task { get; set; }

    [MaxLength(10)]
    public string? TargetLanguage { get; set; }

    [MaxLength(64)]
    public string OperationId { get; set; } = string.Empty;

    public RecipeEnrichmentJobState State { get; set; } = RecipeEnrichmentJobState.Pending;
    public int AttemptCount { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime NotBefore { get; set; } = DateTime.UtcNow;

    [MaxLength(1000)]
    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

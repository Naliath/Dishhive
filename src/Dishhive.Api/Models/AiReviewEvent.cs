using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>Observed human review actions. These are signals, not model-accuracy labels.
/// No recipe/member text is retained; IDs, source versions and changed field names suffice.</summary>
public sealed class AiReviewEvent
{
    public Guid Id { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    [MaxLength(50)] public string Task { get; set; } = "";
    [MaxLength(40)] public string Kind { get; set; } = "";
    [MaxLength(64)] public string? OperationId { get; set; }
    public Guid? RecipeId { get; set; }
    public Guid? BatchId { get; set; }
    public Guid? SuggestionId { get; set; }
    public long? SourceRevision { get; set; }
    public long? ObservedRevision { get; set; }
    public long? SourceConfirmationVersion { get; set; }
    public long? ObservedConfirmationVersion { get; set; }
    [MaxLength(30)] public string? Outcome { get; set; }
    public string ChangedFieldsJson { get; set; } = "[]";
    public string InputChangesJson { get; set; } = "[]";
}

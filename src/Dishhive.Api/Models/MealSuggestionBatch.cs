using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

public class MealSuggestionBatch
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(2);
    [MaxLength(20)] public string Status { get; set; } = "complete";
    [MaxLength(64)] public string? OperationId { get; set; }
    public List<MealSuggestionCandidate> Candidates { get; set; } = [];
}

public class MealSuggestionCandidate
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public MealSuggestionBatch? Batch { get; set; }
    public Guid? RecipeId { get; set; }
    public long? RecipeRevision { get; set; }
    public long? FactsConfirmationVersion { get; set; }
    public string? ReviewSnapshotJson { get; set; }
    [MaxLength(DomainLimits.DishName)] public string DishName { get; set; } = string.Empty;
    [MaxLength(1000)] public string? SourceUrl { get; set; }
    [MaxLength(80)] public string CandidateVersion { get; set; } = string.Empty;
    [MaxLength(100)] public string? FreezyItemRef { get; set; }
    public int FreezyItemQuantity { get; set; }
    public DateOnly Date { get; set; }
    public MealType MealType { get; set; }
    public Course Course { get; set; }
    [MaxLength(30)] public string? ReviewOutcome { get; set; }
    public bool WasModified { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

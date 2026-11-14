namespace Dishhive.Api.Models.DTOs;

public sealed record AiInputRevisionDto(string Kind, string Id, long? Revision, long? ConfirmationVersion, string Fingerprint);

public sealed record AiReviewEventDto(Guid Id, DateTime OccurredAt, string Task, string Kind,
    string? OperationId, Guid? RecipeId, Guid? BatchId, Guid? SuggestionId, string? Outcome,
    long? SourceRevision, long? ObservedRevision, long? SourceConfirmationVersion, long? ObservedConfirmationVersion,
    IReadOnlyList<string> ChangedFields, IReadOnlyList<string> InputChanges);

public sealed record AiReviewMetricsDto(int ReviewActions, int FactsReviews, int FactsCorrections,
    int TextEditsAfterTranslation, int ReviewedSuggestions, int EditedSuggestions, int SuggestionsWithChangedInputs,
    IReadOnlyList<AiReviewEventDto> Events);

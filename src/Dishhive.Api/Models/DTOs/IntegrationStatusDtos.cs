namespace Dishhive.Api.Models.DTOs;

public record IntegrationStatusResponseDto(
    AiIntegrationStatusDto Ai,
    FreezyIntegrationStatusDto Freezy,
    ScraperIntegrationStatusDto Scraper,
    WebSearchIntegrationStatusDto WebSearch
);

public record WebSearchIntegrationStatusDto(
    bool Configured,
    bool Reachable,
    bool Operational,
    string? Provider,
    string? BaseUrl,
    string? Error
);

public record AiIntegrationStatusDto(
    bool Configured,
    bool Reachable,
    string? Provider,
    string? Model,
    string? BaseUrl,
    bool StatsEnabled,
    string ModelTestState,
    string? ModelTestVerdict
);

public record AiModelTestStatusDto(
    string State,
    AiModelTestResultDto? Result
);

public record AiModelTestResultDto(
    DateTimeOffset TestedAt,
    string Verdict,
    string ResponseMode,
    bool EndpointReachable,
    bool? ModelListed,
    bool FormatCompatible,
    bool? EvaluationPassed,
    bool? PlanningQualityPassed,
    double? TokensPerSecond,
    long ElapsedMs,
    IReadOnlyList<AiModelTestCheckDto> Checks
)
{
    public bool PlanningEligible { get; init; }
}

public record AiModelTestCheckDto(string Name, bool Passed, string Detail);

public record AiPlanningMetricsDto(
    int RunCount,
    int SuccessfulRuns,
    int PartialFallbackRuns,
    int FallbackRuns,
    double AverageTotalDurationMs,
    double AverageCompletionDurationMs,
    double P95TotalDurationMs,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalReasoningTokens,
    int TotalSearches,
    int TotalEmptySearches,
    int TotalRecipeResolutions,
    int TotalResolutionFailures,
    int TotalParseFailures);

public record AiTaskMetricsDto(
    int RunCount,
    int SuccessfulRuns,
    int FailedRuns,
    int CancelledRuns,
    int IncompleteRuns,
    double AverageDurationMs,
    double P95DurationMs,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalReasoningTokens,
    IReadOnlyList<AiTaskSummaryDto> Tasks);

public record AiTaskSummaryDto(
    string Task,
    string Provider,
    string Model,
    string ConfigFingerprint,
    int RunCount,
    int SuccessfulRuns,
    int FailedRuns,
    double AverageDurationMs,
    double P95DurationMs,
    long TotalTokens);

public record AiTaskRunDto(
    Guid Id,
    string OperationId,
    string Task,
    string Provider,
    string Model,
    string ConfigFingerprint,
    string ContractVersion,
    DateTime StartedAt,
    string Outcome,
    string ResponseMode,
    string? FinishReason,
    string? Error,
    int Attempt,
    int MaxOutputTokens,
    double Temperature,
    bool ReasoningDisabled,
    bool SchemaRequested,
    bool SchemaApplied,
    long PromptCharacters,
    int InputBudgetTokens,
    long QueueWaitMs,
    string PromptHash,
    long OutputCharacters,
    long InputTokens,
    long OutputTokens,
    long ReasoningTokens,
    long TotalTokens,
    long DurationMs)
{
    public int? OperationAttempt { get; init; }
    public string? InputFingerprint { get; init; }
    public IReadOnlyList<AiInputRevisionDto>? InputRevisions { get; init; }
}

public record AiPlanningRunDto(
    Guid Id,
    string RequestId,
    DateTime StartedAt,
    string Outcome,
    string Provider,
    string Model,
    string ConfigFingerprint,
    string? Instructions,
    string? NormalizedIntentJson,
    string? Error,
    int RequestedDays,
    int SuggestedItems,
    int ExternalSuggestions,
    int FallbackSuggestions,
    bool UsedExternalResearch,
    int CompletionAttempts,
    int ParseFailures,
    int ModelTurns,
    long InputTokens,
    long OutputTokens,
    long ReasoningTokens,
    long TotalTokens,
    int ResearchCalls,
    int SearchCount,
    int EmptySearchCount,
    int SearchResultCount,
    int RecipeResolutionCount,
    int RecipeResolutionFailureCount,
    long CapabilityWaitMs,
    long CompletionDurationMs,
    long SearchDurationMs,
    long RecipeResolutionDurationMs,
    long TotalDurationMs);

public record AiPlanningMetricsResponseDto(
    AiPlanningMetricsDto Summary,
    IReadOnlyList<AiPlanningRunDto> Runs,
    AiTaskMetricsDto TaskMetrics,
    IReadOnlyList<AiTaskRunDto> Calls)
{
    public AiReviewMetricsDto? Reviews { get; init; }
}

public record FreezyIntegrationStatusDto(
    bool Configured,
    bool Reachable,
    string? BaseUrl
);

public record ScraperIntegrationStatusDto(
    bool Configured,
    bool Reachable,
    string? BaseUrl,
    string? PackageVersion
);

public record ScraperVersionCheckDto(
    string InstalledVersion,
    string? LatestVersion,
    bool UpdateAvailable
);

public record ScraperUpdateRequestDto(string? Version);

public record ScraperUpdateStatusDto(
    string Status,
    string? ActiveVersion,
    DateTimeOffset? UpdatedAt,
    string? Error);

public record ScraperUpdateResponseDto(string? TargetVersion);

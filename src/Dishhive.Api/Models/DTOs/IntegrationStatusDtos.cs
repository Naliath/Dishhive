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
    bool? EvaluationPassed,
    double? TokensPerSecond,
    long ElapsedMs,
    IReadOnlyList<AiModelTestCheckDto> Checks
);

public record AiModelTestCheckDto(string Name, bool Passed, string Detail);

public record AiPlanningMetricsDto(
    int RunCount,
    int SuccessfulRuns,
    int FallbackRuns,
    double AverageTotalDurationMs,
    double AverageCompletionDurationMs,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalReasoningTokens,
    int TotalSearches,
    int TotalEmptySearches,
    int TotalRecipeResolutions,
    int TotalResolutionFailures,
    int TotalParseFailures);

public record AiPlanningRunDto(
    Guid Id,
    string RequestId,
    DateTime StartedAt,
    string Outcome,
    string Provider,
    string Model,
    string? Instructions,
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
    IReadOnlyList<AiPlanningRunDto> Runs);

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

public record ScraperUpdateResponseDto(string? TargetVersion);

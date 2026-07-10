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

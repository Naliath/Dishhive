namespace Dishhive.Api.Models.DTOs;

public record IntegrationStatusResponseDto(
    AiIntegrationStatusDto Ai,
    FreezyIntegrationStatusDto Freezy,
    ScraperIntegrationStatusDto Scraper
);

public record AiIntegrationStatusDto(
    bool Configured,
    bool Reachable,
    string? Provider,
    string? Model,
    string? BaseUrl
);

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

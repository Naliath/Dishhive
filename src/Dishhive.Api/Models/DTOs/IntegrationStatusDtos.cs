namespace Dishhive.Api.Models.DTOs;

public record IntegrationStatusResponseDto(
    AiIntegrationStatusDto Ai,
    FreezyIntegrationStatusDto Freezy
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

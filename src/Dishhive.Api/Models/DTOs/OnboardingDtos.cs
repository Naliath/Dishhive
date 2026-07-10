namespace Dishhive.Api.Models.DTOs;

/// <summary>The result of the idempotent onboarding start check.</summary>
public record OnboardingStatusDto(bool ShouldShow);

/// <summary>Marks onboarding as completed normally or deliberately skipped.</summary>
public record CompleteOnboardingDto(bool Skipped);

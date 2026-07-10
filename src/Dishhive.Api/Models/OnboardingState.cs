namespace Dishhive.Api.Models;

/// <summary>
/// The closed lifecycle states of the first-run onboarding wizard. Names are persisted
/// through <see cref="EnumNames"/> and therefore form part of the storage contract.
/// </summary>
internal enum OnboardingState
{
    InProgress,
    Completed,
    Skipped
}

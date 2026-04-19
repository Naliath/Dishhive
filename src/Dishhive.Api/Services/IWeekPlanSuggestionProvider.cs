using Dishhive.Api.Models.DTOs;

namespace Dishhive.Api.Services;

/// <summary>
/// Extension point for AI-assisted or rule-based week plan suggestions.
/// Implement this interface to provide meal suggestions for a given week.
/// The default implementation returns no suggestions (disabled / stub).
/// </summary>
public interface IWeekPlanSuggestionProvider
{
    /// <summary>Human-readable name for the provider, shown in the UI.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Returns true when this provider has suggestions available.
    /// Allows the UI to conditionally show the "Suggest" button.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Generate meal suggestions for the given week.
    /// Returns a list of suggested <see cref="WeekPlannerDtos.PlannedMealDto"/> items
    /// (not yet persisted — the user must accept them before saving).
    /// </summary>
    Task<List<WeekPlannerDtos.UpsertPlannedMealDto>> SuggestMealsAsync(
        DateOnly weekStartDate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default stub implementation: always returns an empty suggestion list.
/// Replace with a real AI or rule-based implementation when ready.
/// </summary>
public class StubWeekPlanSuggestionProvider : IWeekPlanSuggestionProvider
{
    public string ProviderName => "None";
    public bool IsAvailable => false;

    public Task<List<WeekPlannerDtos.UpsertPlannedMealDto>> SuggestMealsAsync(
        DateOnly weekStartDate,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new List<WeekPlannerDtos.UpsertPlannedMealDto>());
}

using Dishhive.Api.Services.Freezy;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>
/// Input for meal suggestions: the planning context a provider may take into account
/// </summary>
public record MealSuggestionRequest
{
    public DateOnly WeekStart { get; init; }
    public IReadOnlyList<Guid> AttendeeIds { get; init; } = [];
    public IReadOnlyList<string> Constraints { get; init; } = [];
    public IReadOnlyList<string> RecentDishNames { get; init; } = [];
    public IReadOnlyList<FrozenItem> AvailableFrozenItems { get; init; } = [];
}

public record MealSuggestion
{
    public DateOnly Date { get; init; }
    public Guid? RecipeId { get; init; }
    public string? DishName { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Extension point for future AI-assisted planning (see docs/features/week-planner.md).
/// The planner depends only on this interface; replacing the registered implementation
/// (e.g. with an LLM-backed provider) requires no planner changes.
/// </summary>
public interface IMealSuggestionService
{
    Task<IReadOnlyList<MealSuggestion>> SuggestAsync(MealSuggestionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation: no suggestions. Deliberately empty — the seam exists,
/// speculative AI implementation does not (see possible-features.md).
/// </summary>
public class NoOpMealSuggestionService : IMealSuggestionService
{
    public Task<IReadOnlyList<MealSuggestion>> SuggestAsync(MealSuggestionRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MealSuggestion>>([]);
    }
}

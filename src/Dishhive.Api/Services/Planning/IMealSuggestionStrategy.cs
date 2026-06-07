using Dishhive.Api.Models.Settings;

namespace Dishhive.Api.Services.Planning;

/// <summary>
/// Future AI-assisted planning seam. Implementations may inspect a meal slot's
/// <c>VagueIntent</c> / <c>IntentTag</c> / attendees / history and return a concrete
/// recipe suggestion. The default implementation returns <c>null</c> — manual planning
/// only — so adding AI later is purely additive.
/// </summary>
public interface IMealSuggestionStrategy
{
    Task<MealSuggestion?> SuggestAsync(MealSuggestionRequest request, CancellationToken cancellationToken = default);
}

public sealed record MealSuggestionRequest(
    string? VagueIntent,
    string? IntentTag,
    IReadOnlyList<Guid> AttendingFamilyMemberIds,
    DateOnly Date);

public sealed record MealSuggestion(
    Guid? RecipeId,
    string DishLabel,
    string Reason);

public sealed class NoopMealSuggestionStrategy : IMealSuggestionStrategy
{
    public Task<MealSuggestion?> SuggestAsync(MealSuggestionRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<MealSuggestion?>(null);
}

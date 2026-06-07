using Dishhive.Api.Services.Planning;

namespace Dishhive.Api.Services.Agents.Planning;

/// <summary>
/// Adapter that bridges the LLM-powered <see cref="IMealPlanningAgent"/> into the
/// existing <see cref="IMealSuggestionStrategy"/> seam. When AI is unavailable,
/// gracefully returns <c>null</c> (preserving the original no-op contract).
/// </summary>
public sealed class AgentMealSuggestionStrategy : IMealSuggestionStrategy
{
    private readonly IMealPlanningAgent _agent;
    private readonly ILogger<AgentMealSuggestionStrategy> _logger;

    public AgentMealSuggestionStrategy(IMealPlanningAgent agent, ILogger<AgentMealSuggestionStrategy> logger)
    {
        _agent = agent;
        _logger = logger;
    }

    public async Task<MealSuggestion?> SuggestAsync(MealSuggestionRequest request, CancellationToken cancellationToken = default)
    {
        if (!_agent.IsAvailable) return null;

        try
        {
            var result = await _agent.SuggestAsync(new MealSuggestionInput(
                VagueIntent: request.VagueIntent,
                IntentTag: request.IntentTag,
                Date: request.Date,
                MealType: "Dinner",
                AttendingFamilyMemberIds: request.AttendingFamilyMemberIds), cancellationToken);

            return result is null
                ? null
                : new MealSuggestion(result.RecipeId, result.DishLabel, result.Reason);
        }
        catch (AgentUnavailableException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Meal-planning agent failed; falling back to no suggestion.");
            return null;
        }
    }
}

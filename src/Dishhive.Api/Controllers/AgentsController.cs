using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Agents;
using Dishhive.Api.Services.Agents.Planning;
using Dishhive.Api.Services.Agents.RecipeImport;
using Microsoft.AspNetCore.Mvc;

namespace Dishhive.Api.Controllers;

/// <summary>
/// Endpoints for AI-assisted features (meal planning, learned recipe sources).
///
/// All endpoints require AI to be enabled in configuration. When disabled, they return
/// <c>503 Service Unavailable</c> with a descriptive message — never a hard exception.
/// </summary>
[ApiController]
[Route("api/agents")]
[Produces("application/json")]
public class AgentsController : ControllerBase
{
    private readonly IChatClientFactory _chatFactory;
    private readonly IMealPlanningAgent _planner;
    private readonly ILearnedSourceStore _learnedStore;

    public AgentsController(
        IChatClientFactory chatFactory,
        IMealPlanningAgent planner,
        ILearnedSourceStore learnedStore)
    {
        _chatFactory = chatFactory;
        _planner = planner;
        _learnedStore = learnedStore;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(AgentStatusDto), StatusCodes.Status200OK)]
    public ActionResult<AgentStatusDto> Status() =>
        Ok(new AgentStatusDto(_chatFactory.IsAvailable, _chatFactory.Provider, _chatFactory.Model));

    [HttpPost("meal-planning/suggest")]
    [ProducesResponseType(typeof(MealSuggestionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MealSuggestionDto>> Suggest(MealSuggestionRequestDto dto, CancellationToken ct)
    {
        if (!_planner.IsAvailable) return Unavailable("Meal-planning agent is disabled.");
        try
        {
            var result = await _planner.SuggestAsync(new MealSuggestionInput(
                dto.VagueIntent, dto.IntentTag, dto.Date, dto.MealType, dto.AttendingFamilyMemberIds), ct);
            return result is null
                ? NoContent()
                : Ok(new MealSuggestionDto(result.RecipeId, result.DishLabel, result.Reason));
        }
        catch (AgentUnavailableException ex)
        {
            return Unavailable(ex.Message);
        }
    }

    [HttpPost("meal-planning/chat")]
    [ProducesResponseType(typeof(ChatReplyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ChatReplyDto>> Chat(ChatRequestDto dto, CancellationToken ct)
    {
        if (!_planner.IsAvailable) return Unavailable("Meal-planning agent is disabled.");
        if (dto.Messages.Count == 0) return BadRequest(new { message = "Messages must not be empty." });
        try
        {
            var conversation = dto.Messages.Select(m => new ChatTurn(m.Role, m.Content)).ToList();
            var reply = await _planner.ChatAsync(conversation, ct);
            return Ok(new ChatReplyDto(reply));
        }
        catch (AgentUnavailableException ex)
        {
            return Unavailable(ex.Message);
        }
    }

    [HttpGet("learned-sources")]
    [ProducesResponseType(typeof(IEnumerable<LearnedRecipeSourceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<LearnedRecipeSourceDto>>> LearnedSources(CancellationToken ct)
    {
        var rows = await _learnedStore.ListAsync(ct);
        return Ok(rows.Select(r => new LearnedRecipeSourceDto(
            r.Host,
            r.ProviderKey,
            Enum.TryParse<Models.Agents.LearnedRecipeSourceStrategy>(r.Strategy, out var s)
                ? s : Models.Agents.LearnedRecipeSourceStrategy.JsonLd,
            r.LearnedAt,
            r.LastUsedAt,
            r.UseCount,
            r.SourceUrl)));
    }

    [HttpDelete("learned-sources/{host}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteLearnedSource(string host, CancellationToken ct) =>
        await _learnedStore.DeleteByHostAsync(host, ct) ? NoContent() : NotFound();

    private ActionResult Unavailable(string message) =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new { message });
}

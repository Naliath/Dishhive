using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.ShoppingList;
using Dishhive.Api.Services.Localization;
using Microsoft.AspNetCore.Mvc;

namespace Dishhive.Api.Controllers;

/// <summary>
/// Shopping list generation from the planned week menu
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ShoppingListController : ControllerBase
{
    private readonly IShoppingListService _shoppingListService;
    private readonly UserMessageLocalizer _messages;

    public ShoppingListController(IShoppingListService shoppingListService, UserMessageLocalizer messages)
    {
        _shoppingListService = shoppingListService;
        _messages = messages;
    }

    /// <summary>
    /// Generate the shopping list for a date range (inclusive)
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ShoppingListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ShoppingListDto>> GetShoppingList(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        if (from > to)
        {
            return BadRequest(new ProblemDetails
            {
                Title = await _messages.GetAsync("common.invalidRangeTitle", HttpContext.RequestAborted),
                Detail = await _messages.GetAsync("common.invalidRangeDetail", HttpContext.RequestAborted)
            });
        }

        return Ok(await _shoppingListService.GenerateAsync(from, to));
    }
}

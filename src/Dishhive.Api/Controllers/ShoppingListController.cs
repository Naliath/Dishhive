using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.ShoppingList;
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

    public ShoppingListController(IShoppingListService shoppingListService)
    {
        _shoppingListService = shoppingListService;
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
            return BadRequest(new ProblemDetails { Title = "Invalid range", Detail = "'from' must be before 'to'." });
        }

        return Ok(await _shoppingListService.GenerateAsync(from, to));
    }
}

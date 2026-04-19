using Dishhive.Api.Services;
using Dishhive.Api.Models.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FreezerController : ControllerBase
{
    private readonly IFreezyIntegrationService _freezy;

    public FreezerController(IFreezyIntegrationService freezy)
    {
        _freezy = freezy;
    }

    /// <summary>
    /// Returns available frozen items from Freezy.
    /// Returns an empty list when Freezy integration is disabled or unavailable.
    /// </summary>
    [HttpGet("items")]
    public async Task<ActionResult<IEnumerable<FrozenItemDto>>> GetItems(CancellationToken cancellationToken)
    {
        var items = await _freezy.GetFrozenItemsAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet("items/{id:guid}")]
    public async Task<ActionResult<FrozenItemDto>> GetItem(Guid id, CancellationToken cancellationToken)
    {
        var item = await _freezy.GetFrozenItemByIdAsync(id, cancellationToken);
        if (item == null)
            return NotFound();

        return Ok(item);
    }

    [HttpGet("status")]
    public ActionResult GetStatus()
    {
        return Ok(new { enabled = _freezy.IsEnabled });
    }
}

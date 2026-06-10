using Dishhive.Api.Services.Freezy;
using Microsoft.AspNetCore.Mvc;

namespace Dishhive.Api.Controllers;

/// <summary>
/// Exposes Freezy frozen-item suggestions for meal planning.
/// The integration is optional; see docs/features/freezy-integration.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FreezerController : ControllerBase
{
    private readonly IFreezyClient _freezyClient;

    public FreezerController(IFreezyClient freezyClient)
    {
        _freezyClient = freezyClient;
    }

    /// <summary>
    /// Frozen items available for planning, soonest-expiring first.
    /// Returns enabled=false with an empty list when Freezy is not configured.
    /// </summary>
    [HttpGet("suggestions")]
    [ProducesResponseType(typeof(FreezerSuggestionsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FreezerSuggestionsDto>> GetSuggestions()
    {
        if (!_freezyClient.IsConfigured)
        {
            return Ok(new FreezerSuggestionsDto { Enabled = false, Items = [] });
        }

        var items = await _freezyClient.GetFrozenItemsAsync();
        return Ok(new FreezerSuggestionsDto { Enabled = true, Items = items });
    }
}

public class FreezerSuggestionsDto
{
    public bool Enabled { get; set; }
    public IReadOnlyList<FrozenItem> Items { get; set; } = [];
}

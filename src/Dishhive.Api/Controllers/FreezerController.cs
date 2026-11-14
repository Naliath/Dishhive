using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Models.DTOs;
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
    [HttpGet("reservations")]
    [ProducesResponseType(typeof(FreezerReconciliationDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FreezerReconciliationDto>> GetReservations(
        [FromServices] FreezerReservationService reservations, CancellationToken token) => Ok(await reservations.CheckAsync(token));

    [HttpPut("reservations/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ResolveReservation(Guid id, ResolveFreezerReservationDto request,
        [FromServices] FreezerReservationService reservations, CancellationToken token)
    {
        await reservations.ResolveAsync(id, request, token);
        return NoContent();
    }
    private readonly FreezerAvailabilityService _availability;

    public FreezerController(FreezerAvailabilityService availability)
    {
        _availability = availability;
    }

    /// <summary>
    /// Frozen items still available for planning (Freezy stock minus what future meals
    /// already reserve), soonest-expiring first. Returns enabled=false with an empty
    /// list when Freezy is not configured.
    /// </summary>
    [HttpGet("suggestions")]
    [ProducesResponseType(typeof(FreezerSuggestionsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FreezerSuggestionsDto>> GetSuggestions()
    {
        if (!_availability.IsConfigured)
        {
            return Ok(new FreezerSuggestionsDto { Enabled = false, Items = [] });
        }

        var items = await _availability.GetAvailableAsync();
        return Ok(new FreezerSuggestionsDto { Enabled = true, Items = items });
    }
}

public class FreezerSuggestionsDto
{
    public bool Enabled { get; set; }
    public IReadOnlyList<FrozenItem> Items { get; set; } = [];
}

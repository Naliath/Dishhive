using Dishhive.Api.Services.FreezyIntegration;
using Microsoft.AspNetCore.Mvc;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/freezy")]
[Produces("application/json")]
public class FreezyIntegrationController : ControllerBase
{
    private readonly IFreezyClient _freezy;

    public FreezyIntegrationController(IFreezyClient freezy)
    {
        _freezy = freezy;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(FreezyStatus), StatusCodes.Status200OK)]
    public async Task<ActionResult<FreezyStatus>> Status(CancellationToken ct)
    {
        var available = await _freezy.IsAvailableAsync(ct);
        return Ok(new FreezyStatus(available));
    }

    [HttpGet("items")]
    [ProducesResponseType(typeof(IReadOnlyList<FrozenItemReference>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FrozenItemReference>>> Items(CancellationToken ct)
    {
        var items = await _freezy.GetFrozenItemsAsync(ct);
        return Ok(items);
    }
}

public sealed record FreezyStatus(bool Available);

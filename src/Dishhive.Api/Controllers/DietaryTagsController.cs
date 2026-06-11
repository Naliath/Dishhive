using Dishhive.Api.Data;
using Dishhive.Api.Models.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

/// <summary>
/// Read-only access to the dietary tag pool (see docs/features/dietary-tags.md).
/// Tags are managed implicitly through family member updates: created when first
/// assigned, removed when no member uses them — so this list always reflects
/// real usage and feeds the tag autocomplete in the UI.
/// </summary>
[ApiController]
[Route("api/dietarytags")]
public class DietaryTagsController : ControllerBase
{
    private readonly DishhiveDbContext _context;

    public DietaryTagsController(DishhiveDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<DietaryTagDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<DietaryTagDto>>> GetTags()
    {
        var tags = await _context.DietaryTags
            .AsNoTracking()
            .OrderBy(t => t.Kind)
            .ThenBy(t => t.Name)
            .Select(t => new DietaryTagDto { Id = t.Id, Name = t.Name, Kind = t.Kind })
            .ToListAsync();

        return Ok(tags);
    }
}

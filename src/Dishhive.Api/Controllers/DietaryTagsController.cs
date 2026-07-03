using Dishhive.Api.Data;
using Dishhive.Api.Models;
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

    /// <summary>
    /// The built-in preset excluded classes for a tag name of a kind, so the family
    /// form can preview what a not-yet-saved tag will mean (the same resolution a
    /// save without explicit classes applies). Empty = not machine-checkable.
    /// </summary>
    [HttpGet("preset")]
    [ProducesResponseType(typeof(DietaryTagPresetDto), StatusCodes.Status200OK)]
    public ActionResult<DietaryTagPresetDto> GetPreset([FromQuery] string? name, [FromQuery] DietaryTagKind kind)
    {
        return Ok(new DietaryTagPresetDto
        {
            ExcludedClasses = IngredientClasses.ToNames(DietaryTagPresets.Resolve(name ?? "", kind))
        });
    }
}

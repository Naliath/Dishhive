using Dishhive.Api.Data;
using Dishhive.Api.Models.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

/// <summary>
/// Read-only access to the recipe tag pool (see docs/features/recipe-organization.md).
/// Tags are managed implicitly through recipe create/update: created when first
/// assigned, removed when no recipe uses them — the list always reflects real usage
/// and feeds the tag filter and autocomplete in the UI.
/// </summary>
[ApiController]
[Route("api/recipetags")]
public class RecipeTagsController : ControllerBase
{
    private readonly DishhiveDbContext _context;

    public RecipeTagsController(DishhiveDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RecipeTagDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<RecipeTagDto>>> GetTags()
    {
        var tags = await _context.RecipeTags
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new RecipeTagDto { Id = t.Id, Name = t.Name })
            .ToListAsync();

        return Ok(tags);
    }
}

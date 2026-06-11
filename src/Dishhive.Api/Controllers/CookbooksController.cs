using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

/// <summary>
/// Cookbooks: named, saved recipe filters (search term + category + tags).
/// See docs/features/recipe-organization.md. The filter itself runs through the
/// normal GET /api/recipes query parameters — a cookbook only stores them.
/// </summary>
[ApiController]
[Route("api/cookbooks")]
public class CookbooksController : ControllerBase
{
    private readonly DishhiveDbContext _context;
    private readonly ILogger<CookbooksController> _logger;

    public CookbooksController(DishhiveDbContext context, ILogger<CookbooksController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CookbookDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CookbookDto>>> GetCookbooks()
    {
        var cookbooks = await _context.Cookbooks
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync();

        return Ok(cookbooks.Select(ToDto));
    }

    [HttpPost]
    [ProducesResponseType(typeof(CookbookDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CookbookDto>> CreateCookbook(CreateCookbookDto dto)
    {
        var validation = await ValidateAsync(dto, excludeId: null);
        if (validation != null)
        {
            return validation;
        }

        var cookbook = new Cookbook();
        Apply(cookbook, dto);

        _context.Cookbooks.Add(cookbook);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created cookbook {Name} ({Id})", cookbook.Name, cookbook.Id);
        return CreatedAtAction(nameof(GetCookbooks), null, ToDto(cookbook));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CookbookDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CookbookDto>> UpdateCookbook(Guid id, UpdateCookbookDto dto)
    {
        var cookbook = await _context.Cookbooks.FindAsync(id);
        if (cookbook == null)
        {
            return NotFound();
        }

        var validation = await ValidateAsync(dto, excludeId: id);
        if (validation != null)
        {
            return validation;
        }

        Apply(cookbook, dto);
        await _context.SaveChangesAsync();

        return Ok(ToDto(cookbook));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCookbook(Guid id)
    {
        var cookbook = await _context.Cookbooks.FindAsync(id);
        if (cookbook == null)
        {
            return NotFound();
        }

        _context.Cookbooks.Remove(cookbook);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted cookbook {Id}", id);
        return NoContent();
    }

    /// <summary>A cookbook needs a unique name and at least one filter criterion</summary>
    private async Task<BadRequestObjectResult?> ValidateAsync(CreateCookbookDto dto, Guid? excludeId)
    {
        var hasFilter = !string.IsNullOrWhiteSpace(dto.SearchTerm)
            || !string.IsNullOrWhiteSpace(dto.Category)
            || dto.Tags.Any(t => !string.IsNullOrWhiteSpace(t));
        if (!hasFilter)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Empty cookbook",
                Detail = "Set a search term, a category or at least one tag."
            });
        }

        var name = dto.Name.Trim().ToLower();
        var nameTaken = await _context.Cookbooks
            .AnyAsync(c => c.Name.ToLower() == name && (excludeId == null || c.Id != excludeId));
        if (nameTaken)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Duplicate cookbook",
                Detail = $"A cookbook named '{dto.Name.Trim()}' already exists."
            });
        }

        return null;
    }

    private static void Apply(Cookbook cookbook, CreateCookbookDto dto)
    {
        cookbook.Name = dto.Name.Trim();
        cookbook.SearchTerm = string.IsNullOrWhiteSpace(dto.SearchTerm) ? null : dto.SearchTerm.Trim();
        cookbook.Category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim();
        cookbook.Tags = dto.Tags
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .DistinctBy(t => t.ToLowerInvariant())
            .ToList();
    }

    private static CookbookDto ToDto(Cookbook cookbook) => new()
    {
        Id = cookbook.Id,
        Name = cookbook.Name,
        SearchTerm = cookbook.SearchTerm,
        Category = cookbook.Category,
        Tags = cookbook.Tags
    };
}

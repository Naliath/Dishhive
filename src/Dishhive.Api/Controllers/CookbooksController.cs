using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Collections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

/// <summary>
/// Collections: named sets of explicitly curated recipes (a recipe can be in any
/// number of them), plus computed read-only "auto" collections. See
/// docs/features/recipe-organization.md. Names may not contain square brackets —
/// they delimit the #[Name] mention syntax in planning instruction fields.
/// </summary>
[ApiController]
[Route("api/cookbooks")]
public class CookbooksController : ControllerBase
{
    private readonly DishhiveDbContext _context;
    private readonly AutoCollectionProvider _autoCollections;
    private readonly ILogger<CookbooksController> _logger;

    public CookbooksController(
        DishhiveDbContext context,
        AutoCollectionProvider autoCollections,
        ILogger<CookbooksController> logger)
    {
        _context = context;
        _autoCollections = autoCollections;
        _logger = logger;
    }

    /// <summary>Manual collections followed by the computed auto collections</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CookbookDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CookbookDto>>> GetCookbooks(CancellationToken cancellationToken)
    {
        var manual = await _context.Cookbooks
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CookbookDto
            {
                Id = c.Id.ToString(),
                Name = c.Name,
                Kind = "manual",
                RecipeCount = c.Entries.Count
            })
            .ToListAsync(cancellationToken);

        var result = new List<CookbookDto>(manual);
        foreach (var auto in await _autoCollections.ListAsync(cancellationToken))
        {
            result.Add(new CookbookDto
            {
                Id = auto.Id,
                Name = auto.Name,
                Kind = "auto",
                RecipeCount = await auto.ApplyFilter(_context.Recipes.AsNoTracking()).CountAsync(cancellationToken)
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// All computed auto collections with their enabled state and member counts,
    /// for the settings management section. Disabled ones are excluded from the
    /// regular cookbooks list, the recipe filter and #[Name] mention resolution.
    /// </summary>
    [HttpGet("auto-collections")]
    [ProducesResponseType(typeof(IEnumerable<AutoCollectionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AutoCollectionDto>>> GetAutoCollections(CancellationToken cancellationToken)
    {
        var result = new List<AutoCollectionDto>();
        foreach (var (collection, enabled) in await _autoCollections.ListWithStateAsync(cancellationToken))
        {
            result.Add(new AutoCollectionDto
            {
                Id = collection.Id,
                Name = collection.Name,
                Enabled = enabled,
                RecipeCount = await collection.ApplyFilter(_context.Recipes.AsNoTracking()).CountAsync(cancellationToken)
            });
        }

        return Ok(result);
    }

    /// <summary>Enables or disables a computed auto collection</summary>
    [HttpPut("auto-collections/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetAutoCollectionEnabled(
        string id, ToggleAutoCollectionDto dto, CancellationToken cancellationToken)
    {
        var known = await _autoCollections.SetEnabledAsync(id, dto.Enabled, cancellationToken);
        return known ? NoContent() : NotFound();
    }

    /// <summary>The recipes in a collection; {id} is a manual Guid or an auto slug</summary>
    [HttpGet("{id}/recipes")]
    [ProducesResponseType(typeof(IEnumerable<RecipeListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<RecipeListItemDto>>> GetCookbookRecipes(
        string id, CancellationToken cancellationToken)
    {
        IQueryable<Recipe> recipes;
        if (Guid.TryParse(id, out var cookbookId))
        {
            if (!await _context.Cookbooks.AnyAsync(c => c.Id == cookbookId, cancellationToken))
            {
                return NotFound();
            }

            recipes = _context.Recipes
                .AsNoTracking()
                .Where(r => _context.CookbookEntries.Any(e => e.CookbookId == cookbookId && e.RecipeId == r.Id));
        }
        else
        {
            var auto = await _autoCollections.FindByIdAsync(id, cancellationToken);
            if (auto == null)
            {
                return NotFound();
            }

            recipes = auto.ApplyFilter(_context.Recipes.AsNoTracking());
        }

        var items = await RecipeListMapping.Project(recipes.OrderBy(r => r.Title)).ToListAsync(cancellationToken);
        RecipeListMapping.ResolveLocalImageUrls(items);
        return Ok(items);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CookbookDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CookbookDto>> CreateCookbook(
        CreateCookbookDto dto, CancellationToken cancellationToken)
    {
        var validation = await ValidateNameAsync(dto.Name, excludeId: null, cancellationToken);
        if (validation != null)
        {
            return validation;
        }

        var cookbook = new Cookbook { Name = dto.Name.Trim() };
        _context.Cookbooks.Add(cookbook);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created collection {Name} ({Id})", cookbook.Name, cookbook.Id);
        return CreatedAtAction(nameof(GetCookbooks), null, ToDto(cookbook, recipeCount: 0));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CookbookDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CookbookDto>> UpdateCookbook(
        Guid id, UpdateCookbookDto dto, CancellationToken cancellationToken)
    {
        var cookbook = await _context.Cookbooks
            .Include(c => c.Entries)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cookbook == null)
        {
            return NotFound();
        }

        var validation = await ValidateNameAsync(dto.Name, excludeId: id, cancellationToken);
        if (validation != null)
        {
            return validation;
        }

        cookbook.Name = dto.Name.Trim();
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(cookbook, cookbook.Entries.Count));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCookbook(Guid id, CancellationToken cancellationToken)
    {
        var cookbook = await _context.Cookbooks
            .Include(c => c.Entries)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cookbook == null)
        {
            return NotFound();
        }

        // Entries are removed explicitly (not via database cascade) so delete
        // semantics are identical across EF providers, like recipe deletion
        _context.CookbookEntries.RemoveRange(cookbook.Entries);
        _context.Cookbooks.Remove(cookbook);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted collection {Id}", id);
        return NoContent();
    }

    /// <summary>Adds recipes to a manual collection; already-present recipes are skipped</summary>
    [HttpPost("{id:guid}/recipes")]
    [ProducesResponseType(typeof(CookbookDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CookbookDto>> AddRecipes(
        Guid id, CookbookRecipesRequestDto dto, CancellationToken cancellationToken)
    {
        var cookbook = await _context.Cookbooks
            .Include(c => c.Entries)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cookbook == null)
        {
            return NotFound();
        }

        var requestedIds = dto.RecipeIds.Distinct().ToList();
        var knownIds = await _context.Recipes
            .Where(r => requestedIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var unknown = requestedIds.Except(knownIds).ToList();
        if (unknown.Count > 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Unknown recipes",
                Detail = $"No recipe found for: {string.Join(", ", unknown)}"
            });
        }

        var existing = cookbook.Entries.Select(e => e.RecipeId).ToHashSet();
        foreach (var recipeId in knownIds.Where(rid => !existing.Contains(rid)))
        {
            cookbook.Entries.Add(new CookbookEntry { CookbookId = id, RecipeId = recipeId });
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(cookbook, cookbook.Entries.Count));
    }

    [HttpDelete("{id:guid}/recipes/{recipeId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveRecipe(Guid id, Guid recipeId, CancellationToken cancellationToken)
    {
        var entry = await _context.CookbookEntries
            .FirstOrDefaultAsync(e => e.CookbookId == id && e.RecipeId == recipeId, cancellationToken);
        if (entry == null)
        {
            return NotFound();
        }

        _context.CookbookEntries.Remove(entry);
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Collection names must be unique, free of the #[…] delimiter characters, and
    /// must not collide with a computed auto-collection name.
    /// </summary>
    private async Task<BadRequestObjectResult?> ValidateNameAsync(
        string rawName, Guid? excludeId, CancellationToken cancellationToken)
    {
        var name = rawName.Trim();
        if (name.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Empty name",
                Detail = "Give the collection a name."
            });
        }

        if (name.Contains('[') || name.Contains(']'))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid collection name",
                Detail = "Square brackets are not allowed in collection names — they delimit #[Name] references in planning instructions."
            });
        }

        var lowered = name.ToLower();
        var nameTaken = await _context.Cookbooks
            .AnyAsync(c => c.Name.ToLower() == lowered && (excludeId == null || c.Id != excludeId), cancellationToken);
        if (nameTaken)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Duplicate collection",
                Detail = $"A collection named '{name}' already exists."
            });
        }

        if (await _autoCollections.IsReservedNameAsync(name, cancellationToken))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Reserved name",
                Detail = $"'{name}' is the name of a built-in automatic collection."
            });
        }

        return null;
    }

    private static CookbookDto ToDto(Cookbook cookbook, int recipeCount) => new()
    {
        Id = cookbook.Id.ToString(),
        Name = cookbook.Name,
        Kind = "manual",
        RecipeCount = recipeCount
    };
}

using Dishhive.Api.Data;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Models.Recipes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/recipes")]
[Produces("application/json")]
public class RecipesController : ControllerBase
{
    private readonly DishhiveDbContext _db;

    public RecipesController(DishhiveDbContext db) => _db = db;

    /// <summary>List recipes with optional case-insensitive title search and tag filter.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RecipeSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<RecipeSummaryDto>>> Search([FromQuery] string? search, [FromQuery] string? tag)
    {
        IQueryable<Recipe> query = _db.Recipes.Include(r => r.Tags);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ToLower-based contains keeps the query portable across PostgreSQL and the InMemory provider used by tests.
            var needle = search.Trim().ToLowerInvariant();
            query = query.Where(r => r.Title.ToLower().Contains(needle)
                                  || (r.Description != null && r.Description.ToLower().Contains(needle)));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var t = tag.Trim();
            query = query.Where(r => r.Tags.Any(x => x.Tag == t));
        }

        var recipes = await query.OrderBy(r => r.Title).ToListAsync();
        return Ok(recipes.Select(r => r.ToSummary()));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RecipeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecipeDto>> Get(Guid id)
    {
        var recipe = await LoadAggregateAsync(id);
        return recipe is null ? NotFound() : Ok(recipe.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(RecipeDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<RecipeDto>> Create(CreateRecipeDto dto)
    {
        var recipe = new Recipe
        {
            Title = dto.Title,
            Description = dto.Description,
            Servings = dto.Servings,
            ImageUrl = dto.ImageUrl,
            VideoUrl = dto.VideoUrl,
            SourceUrl = dto.SourceUrl,
            SourceProviderKey = dto.SourceProviderKey,
            SourceRawPayload = dto.SourceRawPayload,
            Notes = dto.Notes,
        };
        ApplyChildren(recipe, dto);

        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var saved = await LoadAggregateAsync(recipe.Id);
        return CreatedAtAction(nameof(Get), new { id = recipe.Id }, saved!.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(RecipeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecipeDto>> Update(Guid id, UpdateRecipeDto dto)
    {
        var recipe = await LoadAggregateAsync(id);
        if (recipe is null) return NotFound();

        recipe.Title = dto.Title;
        recipe.Description = dto.Description;
        recipe.Servings = dto.Servings;
        recipe.ImageUrl = dto.ImageUrl;
        recipe.VideoUrl = dto.VideoUrl;
        recipe.SourceUrl = dto.SourceUrl;
        recipe.SourceProviderKey = dto.SourceProviderKey;
        recipe.SourceRawPayload = dto.SourceRawPayload;
        recipe.Notes = dto.Notes;

        // Replace children — simplest correct semantics for v1.
        _db.RecipeIngredients.RemoveRange(recipe.Ingredients);
        _db.RecipeSteps.RemoveRange(recipe.Steps);
        _db.RecipeTags.RemoveRange(recipe.Tags);
        recipe.Ingredients.Clear();
        recipe.Steps.Clear();
        recipe.Tags.Clear();
        ApplyChildren(recipe, dto);

        await _db.SaveChangesAsync();

        var reloaded = await LoadAggregateAsync(id);
        return Ok(reloaded!.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var recipe = await _db.Recipes.FindAsync(id);
        if (recipe is null) return NotFound();
        _db.Recipes.Remove(recipe);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("tags")]
    [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<string>>> AllTags()
    {
        var tags = await _db.RecipeTags
            .Select(t => t.Tag)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();
        return Ok(tags);
    }

    private Task<Recipe?> LoadAggregateAsync(Guid id) =>
        _db.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .Include(r => r.Tags)
            .FirstOrDefaultAsync(r => r.Id == id);

    private static void ApplyChildren(Recipe recipe, CreateRecipeDto dto)
    {
        foreach (var i in dto.Ingredients)
        {
            recipe.Ingredients.Add(new RecipeIngredient
            {
                Order = i.Order,
                Name = i.Name,
                Quantity = i.Quantity,
                Unit = i.Unit,
                OriginalQuantity = i.OriginalQuantity,
                OriginalUnit = i.OriginalUnit,
                Section = i.Section,
                Note = i.Note,
            });
        }

        foreach (var s in dto.Steps)
        {
            recipe.Steps.Add(new RecipeStep { Order = s.Order, Text = s.Text });
        }

        foreach (var tag in dto.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            recipe.Tags.Add(new RecipeTag { Tag = tag });
        }
    }
}

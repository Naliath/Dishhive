using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RecipesController : ControllerBase
{
    private readonly DishhiveDbContext _db;
    private readonly IRecipeImportService _importService;
    private readonly IMeasurementConversionService _conversion;
    private readonly ILogger<RecipesController> _logger;

    public RecipesController(
        DishhiveDbContext db,
        IRecipeImportService importService,
        IMeasurementConversionService conversion,
        ILogger<RecipesController> logger)
    {
        _db = db;
        _importService = importService;
        _conversion = conversion;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RecipeDtos.RecipeSummaryDto>>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] string? tag = null)
    {
        var query = _db.Recipes.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(r => r.Title.Contains(search) || (r.Description != null && r.Description.Contains(search)));

        if (!string.IsNullOrWhiteSpace(tag))
            query = query.Where(r => r.Tags.Contains(tag));

        var recipes = await query
            .OrderBy(r => r.Title)
            .Select(r => new RecipeDtos.RecipeSummaryDto(
                r.Id, r.Title, r.Description, r.Servings, r.PictureUrl, r.Tags, r.CreatedAt))
            .ToListAsync();

        return Ok(recipes);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RecipeDtos.RecipeDto>> GetById(Guid id, [FromQuery] string? units = null)
    {
        var recipe = await _db.Recipes
            .Include(r => r.Ingredients.OrderBy(i => i.SortOrder))
            .Include(r => r.Steps.OrderBy(s => s.StepNumber))
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe == null)
            return NotFound();

        return Ok(MapToDto(recipe, units));
    }

    [HttpPost]
    public async Task<ActionResult<RecipeDtos.RecipeDto>> Create([FromBody] RecipeDtos.CreateRecipeDto dto)
    {
        var recipe = new Recipe
        {
            Title = dto.Title,
            Description = dto.Description,
            Servings = dto.Servings,
            PrepTimeMinutes = dto.PrepTimeMinutes,
            CookTimeMinutes = dto.CookTimeMinutes,
            PictureUrl = dto.PictureUrl,
            VideoUrl = dto.VideoUrl,
            Tags = dto.Tags ?? []
        };

        AddIngredientsAndSteps(recipe, dto.Ingredients, dto.Steps);

        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = recipe.Id }, MapToDto(recipe, null));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] RecipeDtos.UpdateRecipeDto dto)
    {
        var recipe = await _db.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe == null)
            return NotFound();

        recipe.Title = dto.Title;
        recipe.Description = dto.Description;
        recipe.Servings = dto.Servings;
        recipe.PrepTimeMinutes = dto.PrepTimeMinutes;
        recipe.CookTimeMinutes = dto.CookTimeMinutes;
        recipe.PictureUrl = dto.PictureUrl;
        recipe.VideoUrl = dto.VideoUrl;
        recipe.Tags = dto.Tags ?? [];

        // Replace ingredients and steps
        _db.RecipeIngredients.RemoveRange(recipe.Ingredients);
        _db.RecipeSteps.RemoveRange(recipe.Steps);
        recipe.Ingredients = [];
        recipe.Steps = [];

        AddIngredientsAndSteps(recipe, dto.Ingredients, dto.Steps);

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var recipe = await _db.Recipes.FindAsync(id);
        if (recipe == null)
            return NotFound();

        _db.Recipes.Remove(recipe);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("import")]
    public async Task<ActionResult<ImportedRecipeDto>> Import([FromBody] RecipeDtos.ImportRecipeRequestDto dto)
    {
        var result = await _importService.ImportAsync(dto.Url);
        if (result == null)
            return BadRequest(new { message = "Could not import recipe from the provided URL. The URL may not be supported or the page could not be parsed." });

        return Ok(result);
    }

    [HttpPost("import/save")]
    public async Task<ActionResult<RecipeDtos.RecipeDto>> ImportAndSave([FromBody] ImportedRecipeDto imported)
    {
        var recipe = new Recipe
        {
            Title = imported.Title,
            Description = imported.Description,
            Servings = imported.Servings ?? 4,
            PictureUrl = imported.PictureUrl,
            VideoUrl = imported.VideoUrl,
            SourceUrl = imported.SourceUrl,
            SourceName = imported.SourceName,
            SourceRawData = imported.SourceRawData,
            Ingredients = imported.Ingredients.Select((ing, i) => new RecipeIngredient
            {
                Name = ing.Name,
                OriginalQuantity = ing.OriginalQuantity,
                OriginalUnit = ing.OriginalUnit,
                SortOrder = i
            }).ToList(),
            Steps = imported.Steps.Select((step, i) => new RecipeStep
            {
                StepNumber = i + 1,
                Instruction = step
            }).ToList()
        };

        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        // Reload with navigation properties
        await _db.Entry(recipe).Collection(r => r.Ingredients).LoadAsync();
        await _db.Entry(recipe).Collection(r => r.Steps).LoadAsync();

        return CreatedAtAction(nameof(GetById), new { id = recipe.Id }, MapToDto(recipe, null));
    }

    private static void AddIngredientsAndSteps(
        Recipe recipe,
        List<RecipeDtos.CreateRecipeIngredientDto>? ingredients,
        List<RecipeDtos.CreateRecipeStepDto>? steps)
    {
        if (ingredients != null)
        {
            recipe.Ingredients = ingredients.Select(i => new RecipeIngredient
            {
                Name = i.Name,
                Quantity = i.Quantity,
                Unit = i.Unit,
                OriginalQuantity = i.OriginalQuantity,
                OriginalUnit = i.OriginalUnit,
                Notes = i.Notes,
                SortOrder = i.SortOrder
            }).ToList();
        }

        if (steps != null)
        {
            recipe.Steps = steps.Select(s => new RecipeStep
            {
                StepNumber = s.StepNumber,
                Instruction = s.Instruction
            }).ToList();
        }
    }

    private RecipeDtos.RecipeDto MapToDto(Recipe r, string? unitSystem = null) => new(
        r.Id,
        r.Title,
        r.Description,
        r.Servings,
        r.PrepTimeMinutes,
        r.CookTimeMinutes,
        r.PictureUrl,
        r.VideoUrl,
        r.SourceUrl,
        r.SourceName,
        r.Tags,
        r.Ingredients.OrderBy(i => i.SortOrder).Select(i =>
        {
            var (q, u) = unitSystem != null
                ? _conversion.Convert(i.Quantity, i.Unit, unitSystem)
                : (i.Quantity, i.Unit);

            return new RecipeDtos.RecipeIngredientDto(
                i.Id, i.Name,
                q,
                u,
                i.OriginalQuantity, i.OriginalUnit, i.Notes, i.SortOrder);
        }).ToList(),
        r.Steps.OrderBy(s => s.StepNumber).Select(s => new RecipeDtos.RecipeStepDto(
            s.Id, s.StepNumber, s.Instruction)).ToList(),
        r.CreatedAt,
        r.UpdatedAt
    );
}

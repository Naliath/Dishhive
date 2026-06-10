using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Import;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

/// <summary>
/// Controller for the household recipe store, including URL import
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class RecipesController : ControllerBase
{
    private readonly DishhiveDbContext _context;
    private readonly IRecipeImportService _importService;
    private readonly ILogger<RecipesController> _logger;

    public RecipesController(
        DishhiveDbContext context,
        IRecipeImportService importService,
        ILogger<RecipesController> logger)
    {
        _context = context;
        _importService = importService;
        _logger = logger;
    }

    /// <summary>
    /// List recipes, optionally filtered by a title/keyword search term
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RecipeListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<RecipeListItemDto>>> GetRecipes([FromQuery] string? search = null)
    {
        var query = _context.Recipes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(r =>
                r.Title.ToLower().Contains(term) ||
                (r.Keywords != null && r.Keywords.ToLower().Contains(term)));
        }

        var recipes = await query
            .OrderBy(r => r.Title)
            .Select(r => new RecipeListItemDto
            {
                Id = r.Id,
                Title = r.Title,
                Servings = r.Servings,
                TotalTimeMinutes = r.TotalTimeMinutes,
                Category = r.Category,
                ImageUrl = r.ImageData != null ? null : r.ImageUrl,
                HasLocalImage = r.ImageData != null,
                SourceProvider = r.SourceProvider
            })
            .ToListAsync();

        foreach (var recipe in recipes.Where(r => r.HasLocalImage))
        {
            recipe.ImageUrl = ImageEndpoint(recipe.Id);
        }

        return Ok(recipes);
    }

    /// <summary>
    /// Serves the locally stored recipe image (downloaded at import time)
    /// </summary>
    [HttpGet("{id:guid}/image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRecipeImage(Guid id)
    {
        var image = await _context.Recipes
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.ImageData, r.ImageContentType })
            .FirstOrDefaultAsync();

        if (image?.ImageData == null)
        {
            return NotFound();
        }

        return File(image.ImageData, image.ImageContentType ?? "image/jpeg");
    }

    private static string ImageEndpoint(Guid id) => $"/api/recipes/{id}/image";

    /// <summary>
    /// Get a full recipe by id
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RecipeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecipeDto>> GetRecipe(Guid id)
    {
        var recipe = await _context.Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe == null)
        {
            return NotFound();
        }

        return Ok(ToDto(recipe));
    }

    /// <summary>
    /// Create a recipe manually
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RecipeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RecipeDto>> CreateRecipe(CreateRecipeDto dto)
    {
        var recipe = new Recipe();
        ApplyDto(recipe, dto);

        _context.Recipes.Add(recipe);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created recipe {Title} ({Id})", recipe.Title, recipe.Id);
        return CreatedAtAction(nameof(GetRecipe), new { id = recipe.Id }, ToDto(recipe));
    }

    /// <summary>
    /// Update a recipe. Ingredients and steps are replaced wholesale.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(RecipeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecipeDto>> UpdateRecipe(Guid id, UpdateRecipeDto dto)
    {
        var recipe = await _context.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe == null)
        {
            return NotFound();
        }

        _context.RecipeIngredients.RemoveRange(recipe.Ingredients);
        _context.RecipeSteps.RemoveRange(recipe.Steps);
        recipe.Ingredients.Clear();
        recipe.Steps.Clear();

        ApplyDto(recipe, dto);
        await _context.SaveChangesAsync();

        return Ok(ToDto(recipe));
    }

    /// <summary>
    /// Delete a recipe. Planned meals that referenced it keep their denormalized dish name.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRecipe(Guid id)
    {
        // Children and planner references are handled client-side (not via database
        // cascade) so delete semantics are explicit and identical across providers
        var recipe = await _context.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe == null)
        {
            return NotFound();
        }

        var referencingMeals = await _context.PlannedMeals
            .Where(m => m.RecipeId == id)
            .ToListAsync();
        foreach (var meal in referencingMeals)
        {
            meal.RecipeId = null; // DishName stays denormalized, history survives
        }

        var referencingFavorites = await _context.FamilyMemberFavorites
            .Where(f => f.RecipeId == id)
            .ToListAsync();
        foreach (var favorite in referencingFavorites)
        {
            favorite.RecipeId = null; // DishName stays denormalized, favorite survives
        }

        _context.Recipes.Remove(recipe);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted recipe {Id}", id);
        return NoContent();
    }

    /// <summary>
    /// Import a recipe from a supported external source URL.
    /// Re-importing an already imported URL updates the existing recipe.
    /// </summary>
    [HttpPost("import")]
    [ProducesResponseType(typeof(RecipeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RecipeDto>> ImportRecipe(ImportRecipeRequestDto dto)
    {
        try
        {
            var recipe = await _importService.ImportAsync(dto.Url);
            return CreatedAtAction(nameof(GetRecipe), new { id = recipe.Id }, ToDto(recipe));
        }
        catch (UnsupportedRecipeSourceException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Unsupported recipe source", Detail = ex.Message });
        }
        catch (RecipeExtractionFailedException ex)
        {
            return UnprocessableEntity(new ProblemDetails { Title = "No recipe found on page", Detail = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch recipe page {Url}", dto.Url);
            return UnprocessableEntity(new ProblemDetails { Title = "Could not fetch page", Detail = ex.Message });
        }
    }

    private static void ApplyDto(Recipe recipe, CreateRecipeDto dto)
    {
        recipe.Title = dto.Title;
        recipe.Description = dto.Description;
        recipe.Servings = dto.Servings;
        recipe.PrepTimeMinutes = dto.PrepTimeMinutes;
        recipe.CookTimeMinutes = dto.CookTimeMinutes;
        recipe.TotalTimeMinutes = dto.TotalTimeMinutes;
        recipe.Category = dto.Category;
        recipe.Keywords = dto.Keywords;
        recipe.ImageUrl = dto.ImageUrl;
        recipe.VideoUrl = dto.VideoUrl;

        var sortOrder = 0;
        foreach (var ingredient in dto.Ingredients)
        {
            recipe.Ingredients.Add(new RecipeIngredient
            {
                SortOrder = sortOrder++,
                Name = ingredient.Name,
                Quantity = ingredient.Quantity,
                Unit = ingredient.Unit,
                OriginalText = ingredient.OriginalText
                    ?? $"{ingredient.Quantity} {ingredient.Unit} {ingredient.Name}".Trim(),
                OriginalQuantity = ingredient.Quantity,
                OriginalUnit = ingredient.Unit
            });
        }

        var stepNumber = 1;
        foreach (var step in dto.Steps)
        {
            recipe.Steps.Add(new RecipeStep
            {
                StepNumber = stepNumber++,
                Instruction = step.Instruction
            });
        }
    }

    private static RecipeDto ToDto(Recipe recipe) => new()
    {
        Id = recipe.Id,
        Title = recipe.Title,
        Description = recipe.Description,
        Servings = recipe.Servings,
        PrepTimeMinutes = recipe.PrepTimeMinutes,
        CookTimeMinutes = recipe.CookTimeMinutes,
        TotalTimeMinutes = recipe.TotalTimeMinutes,
        Category = recipe.Category,
        Keywords = recipe.Keywords,
        // Prefer the locally stored copy; the source URL is the fallback for
        // manual recipes or when the download failed at import time
        ImageUrl = recipe.ImageData != null ? ImageEndpoint(recipe.Id) : recipe.ImageUrl,
        HasLocalImage = recipe.ImageData != null,
        VideoUrl = recipe.VideoUrl,
        SourceUrl = recipe.SourceUrl,
        SourceProvider = recipe.SourceProvider,
        CreatedAt = recipe.CreatedAt,
        UpdatedAt = recipe.UpdatedAt,
        Ingredients = recipe.Ingredients
            .OrderBy(i => i.SortOrder)
            .Select(i => new RecipeIngredientDto
            {
                Id = i.Id,
                SortOrder = i.SortOrder,
                Name = i.Name,
                Quantity = i.Quantity,
                Unit = i.Unit,
                OriginalText = i.OriginalText,
                OriginalQuantity = i.OriginalQuantity,
                OriginalUnit = i.OriginalUnit
            })
            .ToList(),
        Steps = recipe.Steps
            .OrderBy(s => s.StepNumber)
            .Select(s => new RecipeStepDto
            {
                Id = s.Id,
                StepNumber = s.StepNumber,
                Instruction = s.Instruction
            })
            .ToList()
    };
}

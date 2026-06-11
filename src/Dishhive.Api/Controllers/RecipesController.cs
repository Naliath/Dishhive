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
    /// List recipes, optionally filtered by a title/keyword search term, a category
    /// and/or tags (comma-separated names; a recipe must carry all of them)
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RecipeListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<RecipeListItemDto>>> GetRecipes(
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        [FromQuery] string? tags = null)
    {
        var query = _context.Recipes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(r =>
                r.Title.ToLower().Contains(term) ||
                (r.Keywords != null && r.Keywords.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var wanted = category.Trim().ToLower();
            query = query.Where(r => r.Category != null && r.Category.ToLower() == wanted);
        }

        foreach (var tag in SplitTags(tags))
        {
            var wanted = tag.ToLower();
            query = query.Where(r => r.Tags.Any(a => a.RecipeTag!.Name.ToLower() == wanted));
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
                SourceProvider = r.SourceProvider,
                Tags = r.Tags.Select(a => a.RecipeTag!.Name).OrderBy(n => n).ToList()
            })
            .ToListAsync();

        foreach (var recipe in recipes.Where(r => r.HasLocalImage))
        {
            recipe.ImageUrl = ImageEndpoint(recipe.Id);
        }

        return Ok(recipes);
    }

    /// <summary>
    /// Distinct recipe categories in use, for the library filter
    /// </summary>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<string>>> GetCategories()
    {
        var categories = await _context.Recipes
            .AsNoTracking()
            .Where(r => r.Category != null && r.Category != "")
            .Select(r => r.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        return Ok(categories);
    }

    /// <summary>
    /// Distinct ingredient names in use, for the recipe form autocomplete.
    /// Helps converge on one spelling per ingredient ("ei" vs "eieren").
    /// </summary>
    [HttpGet("ingredients")]
    [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<string>>> GetIngredientNames()
    {
        var names = await _context.Recipes
            .AsNoTracking()
            .SelectMany(r => r.Ingredients)
            .Select(i => i.Name)
            .Where(n => n != "")
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();

        // Postgres DISTINCT is case-sensitive; collapse spelling-case variants here
        var deduped = names
            .GroupBy(n => n.ToLowerInvariant())
            .Select(g => g.First());

        return Ok(deduped);
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
            .Include(r => r.Tags).ThenInclude(a => a.RecipeTag)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe == null)
        {
            return NotFound();
        }

        return Ok(ToDto(recipe));
    }

    /// <summary>
    /// Create a recipe manually. Tags are created on the fly and reused
    /// case-insensitively across recipes.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RecipeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RecipeDto>> CreateRecipe(CreateRecipeDto dto)
    {
        if (dto.Tags.Any(t => t.Trim().Length > 50))
        {
            return TagTooLong();
        }

        var recipe = new Recipe();
        ApplyDto(recipe, dto);

        _context.Recipes.Add(recipe);
        await SyncTagsAsync(recipe, dto.Tags);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created recipe {Title} ({Id})", recipe.Title, recipe.Id);
        return CreatedAtAction(nameof(GetRecipe), new { id = recipe.Id }, ToDto(recipe));
    }

    /// <summary>
    /// Update a recipe. Ingredients and steps are replaced wholesale; tags are
    /// synced to the submitted list (unused tags leave the pool).
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(RecipeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecipeDto>> UpdateRecipe(Guid id, UpdateRecipeDto dto)
    {
        if (dto.Tags.Any(t => t.Trim().Length > 50))
        {
            return TagTooLong();
        }

        var recipe = await _context.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .Include(r => r.Tags).ThenInclude(a => a.RecipeTag)
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
        await SyncTagsAsync(recipe, dto.Tags);
        await _context.SaveChangesAsync();
        await RemoveOrphanedTagsAsync();

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

        var tagAssignments = await _context.RecipeTagAssignments
            .Where(a => a.RecipeId == id)
            .ToListAsync();
        _context.RecipeTagAssignments.RemoveRange(tagAssignments);

        _context.Recipes.Remove(recipe);
        await _context.SaveChangesAsync();
        await RemoveOrphanedTagsAsync();

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

    /// <summary>
    /// Syncs a recipe's tag links to the submitted names: missing tags are created
    /// (reused case-insensitively), removed names unlinked. Matching is by name —
    /// never by entity id, whose generation timing differs between EF providers.
    /// </summary>
    private async Task SyncTagsAsync(Recipe recipe, List<string> tagNames)
    {
        var targets = tagNames
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .DistinctBy(n => n.ToLowerInvariant())
            .ToList();
        var targetKeys = targets.Select(n => n.ToLowerInvariant()).ToHashSet();

        var obsolete = recipe.Tags
            .Where(a => a.RecipeTag == null || !targetKeys.Contains(a.RecipeTag.Name.ToLowerInvariant()))
            .ToList();
        foreach (var assignment in obsolete)
        {
            recipe.Tags.Remove(assignment);
            _context.RecipeTagAssignments.Remove(assignment);
        }

        var existingTags = await _context.RecipeTags.ToListAsync();
        foreach (var name in targets)
        {
            var alreadyLinked = recipe.Tags.Any(a =>
                a.RecipeTag != null && string.Equals(a.RecipeTag.Name, name, StringComparison.OrdinalIgnoreCase));
            if (alreadyLinked)
            {
                continue;
            }

            var tag = existingTags.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (tag == null)
            {
                tag = new RecipeTag { Name = name };
                _context.RecipeTags.Add(tag);
                existingTags.Add(tag);
            }

            recipe.Tags.Add(new RecipeTagAssignment { Recipe = recipe, RecipeTag = tag });
        }
    }

    /// <summary>Tags are kept only while at least one recipe uses them</summary>
    private async Task RemoveOrphanedTagsAsync()
    {
        var orphans = await _context.RecipeTags
            .Where(t => !_context.RecipeTagAssignments.Any(a => a.RecipeTagId == t.Id))
            .ToListAsync();

        if (orphans.Count > 0)
        {
            _context.RecipeTags.RemoveRange(orphans);
            await _context.SaveChangesAsync();
        }
    }

    private static List<string> SplitTags(string? tags) => (tags ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();

    private BadRequestObjectResult TagTooLong() => BadRequest(new ProblemDetails
    {
        Title = "Tag too long",
        Detail = "Tags are at most 50 characters."
    });

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
            .ToList(),
        Tags = recipe.Tags
            .Where(a => a.RecipeTag != null)
            .Select(a => a.RecipeTag!.Name)
            .OrderBy(n => n)
            .ToList()
    };
}

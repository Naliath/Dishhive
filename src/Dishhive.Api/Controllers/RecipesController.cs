using Dishhive.Api.Data;
using Dishhive.Api.Services.Recipes;
using static Dishhive.Api.Services.Recipes.RecipeDtoMapper;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services;
using Dishhive.Api.Services.Collections;
using Dishhive.Api.Services.Facts;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly IRecipeExchangeService _exchangeService;
    private readonly RecipeFactsAssessmentService _factsQueue;
    private readonly RecipeMutationService _mutations;
    private readonly ILogger<RecipesController> _logger;
    private readonly UserMessageLocalizer _messages;

    public RecipesController(
        DishhiveDbContext context,
        IRecipeImportService importService,
        IRecipeExchangeService exchangeService,
        RecipeFactsAssessmentService factsQueue,
        RecipeMutationService mutations,
        UserMessageLocalizer messages,
        ILogger<RecipesController> logger)
    {
        _context = context;
        _importService = importService;
        _exchangeService = exchangeService;
        _factsQueue = factsQueue;
        _mutations = mutations;
        _messages = messages;
        _logger = logger;
    }

    /// <summary>
    /// List recipes, optionally filtered by a title/keyword search term, a category,
    /// tags (comma-separated names; a recipe must carry all of them) and/or a
    /// collection (manual cookbook Guid or auto slug like "auto-quick")
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RecipeListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<RecipeListItemDto>>> GetRecipes(
        [FromServices] AutoCollectionProvider autoCollections,
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        [FromQuery] string? tags = null,
        [FromQuery] string? cookbookId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await QueryRecipes(autoCollections, search, category, tags, cookbookId, page, pageSize, cancellationToken);
        if (result.Result != null) return result.Result;
        Response.Headers["X-Total-Count"] = result.Value!.TotalCount.ToString();
        return Ok(result.Value.Items);
    }

    [HttpGet("page")]
    [ProducesResponseType(typeof(RecipePageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<RecipePageDto>> GetRecipesPage(
        [FromServices] AutoCollectionProvider autoCollections,
        [FromQuery] string? search = null, [FromQuery] string? category = null,
        [FromQuery] string? tags = null, [FromQuery] string? cookbookId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default) =>
        QueryRecipes(autoCollections, search, category, tags, cookbookId, page, pageSize, cancellationToken);

    private async Task<ActionResult<RecipePageDto>> QueryRecipes(
        AutoCollectionProvider autoCollections, string? search, string? category,
        string? tags, string? cookbookId, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
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

        if (!string.IsNullOrWhiteSpace(cookbookId))
        {
            if (Guid.TryParse(cookbookId, out var id))
            {
                if (!await _context.Cookbooks.AnyAsync(c => c.Id == id, cancellationToken))
                {
                    return NotFound();
                }

                query = query.Where(r => _context.CookbookEntries.Any(e => e.CookbookId == id && e.RecipeId == r.Id));
            }
            else
            {
                var auto = await autoCollections.FindByIdAsync(cookbookId, cancellationToken);
                if (auto == null)
                {
                    return NotFound();
                }

                query = auto.ApplyFilter(query);
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var recipes = await RecipeListMapping.Project(
            query.OrderBy(r => r.Title).Skip((page - 1) * pageSize).Take(pageSize),
            _context.CookbookEntries.AsNoTracking())
            .ToListAsync(cancellationToken);
        RecipeListMapping.ResolveLocalImageUrls(recipes);
        return new RecipePageDto { Items = recipes, TotalCount = totalCount, Page = page, PageSize = pageSize };
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

    /// <summary>
    /// Replaces a recipe image from a local file or browser camera capture. The image
    /// is normalized before storage and any old remote source URL is cleared.
    /// </summary>
    [HttpPut("{id:guid}/image")]
    [Consumes("multipart/form-data")]
    [RequestFormLimits(MultipartBodyLengthLimit = RecipeImageProcessor.MaxSourceBytes + 1024 * 64)]
    [RequestSizeLimit(RecipeImageProcessor.MaxSourceBytes + 1024 * 64)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetRecipeImage(
        Guid id, IFormFile? file, CancellationToken cancellationToken)
    {
        await using var stream = file?.OpenReadStream();
        await _mutations.SetRecipeImage(id, stream, file?.Length ?? 0, cancellationToken);
        return NoContent();
    }

    /// <summary>Removes both the local recipe image and its remote source reference</summary>
    [HttpDelete("{id:guid}/image")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRecipeImage(Guid id, CancellationToken cancellationToken)
    {
        await _mutations.DeleteRecipeImage(id, cancellationToken);
        return NoContent();
    }


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
            .Include(r => r.DietaryFacts)
            .Include(r => r.DietaryFactsHistory)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe == null)
        {
            return NotFound();
        }

        var dto = ToDto(recipe);
        dto.CookbookIds = await _context.CookbookEntries
            .Where(e => e.RecipeId == id)
            .Select(e => e.CookbookId)
            .ToListAsync();
        return Ok(dto);
    }

    /// <summary>
    /// Syncs a recipe's collection memberships to the submitted list (manual
    /// collections only — auto collections are computed and read-only)
    /// </summary>
    [HttpPut("{id:guid}/cookbooks")]
    [ProducesResponseType(typeof(IEnumerable<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<Guid>>> SetRecipeCookbooks(
        Guid id, RecipeCookbooksRequestDto dto, CancellationToken cancellationToken)
    {
        return Ok(await _mutations.SetRecipeCookbooks(id, dto, cancellationToken));
    }

    /// <summary>
    /// Create a recipe manually. Tags are created on the fly and reused
    /// case-insensitively across recipes.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RecipeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RecipeDto>> CreateRecipe(
        CreateRecipeDto dto, CancellationToken cancellationToken)
    {
        var recipe = await _mutations.CreateRecipe(dto, cancellationToken);
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
    public async Task<ActionResult<RecipeDto>> UpdateRecipe(
        Guid id, UpdateRecipeDto dto, CancellationToken cancellationToken)
    {
        return Ok(ToDto(await _mutations.UpdateRecipe(id, dto, cancellationToken)));
    }

    /// <summary>
    /// Delete a recipe. Planned meals that referenced it keep their denormalized dish name.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRecipe(Guid id)
    {
        await _mutations.DeleteRecipe(id, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>
    /// Import a recipe from a supported external source URL.
    /// Re-importing an already imported URL updates the existing recipe.
    /// </summary>
    [HttpPost("import")]
    [EnableRateLimiting("expensive")]
    [ProducesResponseType(typeof(RecipeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RecipeDto>> ImportRecipe(
        ImportRecipeRequestDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var recipe = await _importService.ImportAsync(dto.Url, cancellationToken);
            return CreatedAtAction(nameof(GetRecipe), new { id = recipe.Id }, ToDto(recipe));
        }
        catch (UnsupportedRecipeSourceException ex)
        {
            _logger.LogInformation(ex, "Unsupported recipe source {Url}", dto.Url);
            return BadRequest(new ProblemDetails
            {
                Title = await _messages.GetAsync("recipe.unsupportedSourceTitle", cancellationToken),
                Detail = await _messages.GetAsync("recipe.unsupportedSourceDetail", cancellationToken)
            });
        }
        catch (RecipeExtractionFailedException ex)
        {
            _logger.LogInformation(ex, "No recipe found at {Url}", dto.Url);
            return UnprocessableEntity(new ProblemDetails
            {
                Title = await _messages.GetAsync("recipe.notFoundOnPageTitle", cancellationToken),
                Detail = await _messages.GetAsync("recipe.notFoundOnPageDetail", cancellationToken)
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch recipe page {Url}", dto.Url);
            return UnprocessableEntity(new ProblemDetails
            {
                Title = await _messages.GetAsync("recipe.fetchFailedTitle", cancellationToken),
                Detail = await _messages.GetAsync("recipe.fetchFailedDetail", cancellationToken)
            });
        }
    }

    /// <summary>
    /// Sets a recipe's dietary facts explicitly (the review/edit affordance on the
    /// detail page). The facts become UserConfirmed: the background assessment will
    /// no longer overwrite them unless the ingredients change.
    /// </summary>
    [HttpPut("{id:guid}/facts")]
    [ProducesResponseType(typeof(RecipeDietaryFactsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecipeDietaryFactsDto>> SetRecipeFacts(Guid id, UpdateRecipeFactsDto dto)
    {
        return Ok(FactsDto(await _mutations.SetRecipeFacts(id, dto, HttpContext.RequestAborted)));
    }

    /// <summary>
    /// Library-wide dietary-facts progress: assessed/unassessed counts plus the
    /// background queue state. Polled by the settings page while a backfill runs.
    /// </summary>
    [HttpGet("facts/status")]
    [ProducesResponseType(typeof(RecipeFactsStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RecipeFactsStatusDto>> GetFactsStatus(
        [FromServices] IRecipeFactsExtractor extractor, CancellationToken cancellationToken)
    {
        var counts = await _context.Recipes
            .GroupBy(r => r.DietaryFactsStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var queue = _factsQueue.Status;

        return Ok(new RecipeFactsStatusDto
        {
            Unassessed = counts.FirstOrDefault(c => c.Status == DietaryFactsStatus.Unassessed)?.Count ?? 0,
            AiDetected = counts.FirstOrDefault(c => c.Status == DietaryFactsStatus.AiDetected)?.Count ?? 0,
            UserConfirmed = counts.FirstOrDefault(c => c.Status == DietaryFactsStatus.UserConfirmed)?.Count ?? 0,
            Stale = counts.FirstOrDefault(c => c.Status == DietaryFactsStatus.Stale)?.Count ?? 0,
            Pending = counts.FirstOrDefault(c => c.Status == DietaryFactsStatus.Pending)?.Count ?? 0,
            Failed = counts.FirstOrDefault(c => c.Status == DietaryFactsStatus.Failed)?.Count ?? 0,
            QueueDepth = queue.QueueDepth,
            Running = queue.Running,
            Available = extractor.IsAvailable,
            LastError = queue.LastError
        });
    }

    /// <summary>
    /// Queues every still-unassessed recipe for AI facts assessment (the settings
    /// page backfill). includeAssessed explicitly refreshes AI-detected assessments;
    /// user confirmations remain protected.
    /// </summary>
    [HttpPost("facts/backfill")]
    [EnableRateLimiting("expensive")]
    [ProducesResponseType(typeof(RecipeFactsBackfillResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RecipeFactsBackfillResultDto>> BackfillFacts(
        CancellationToken cancellationToken, [FromQuery] bool includeAssessed = false)
    {
        var unassessedIds = await _context.Recipes
            .Where(r => r.DietaryFactsStatus == DietaryFactsStatus.Unassessed
                || r.DietaryFactsStatus == DietaryFactsStatus.Stale
                || r.DietaryFactsStatus == DietaryFactsStatus.Failed
                || (includeAssessed && r.DietaryFactsStatus == DietaryFactsStatus.AiDetected))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var enqueued = await _factsQueue.EnqueueManyAsync(unassessedIds, cancellationToken);
        _logger.LogInformation("Facts backfill requested: {Enqueued} of {Total} eligible recipes queued; includeAssessed={IncludeAssessed}",
            enqueued, unassessedIds.Count, includeAssessed);
        return Ok(new RecipeFactsBackfillResultDto { Enqueued = enqueued });
    }

    /// <summary>
    /// Lists the recipe sources the app knows about (dedicated providers + hosts
    /// already imported from), for the week-planner's @[Source] autocomplete.
    /// </summary>
    [HttpGet("sources")]
    [ProducesResponseType(typeof(IEnumerable<RecipeSourceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<RecipeSourceDto>>> GetSources(
        [FromServices] RecipeSourceCatalog catalog, CancellationToken cancellationToken)
    {
        var sources = await catalog.ListAsync(cancellationToken);
        return Ok(sources.Select(s => new RecipeSourceDto(s.Name, s.Host)));
    }

    /// <summary>
    /// Downloads the whole recipe library as a schema.org Recipe JSON file —
    /// the interchange format other recipe managers understand. Locally stored
    /// images are embedded so the file is self-contained.
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportRecipes()
    {
        var json = await _exchangeService.ExportAsync(HttpContext.RequestAborted);
        var fileName = $"dishhive-recipes-{DateTime.UtcNow:yyyy-MM-dd}.json";
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", fileName);
    }

    /// <summary>
    /// Imports recipes from a schema.org Recipe JSON file (including Dishhive's own
    /// export). Recipes with a known source URL are updated; recipes whose title is
    /// already in the library are skipped; the rest are created.
    /// </summary>
    [HttpPost("import/file")]
    [EnableRateLimiting("expensive")]
    [ProducesResponseType(typeof(RecipeFileImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RecipeFileImportResultDto>> ImportRecipesFile(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = await _messages.GetAsync("recipe.noFileTitle", HttpContext.RequestAborted),
                Detail = await _messages.GetAsync("recipe.noFileDetail", HttpContext.RequestAborted)
            });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _exchangeService.ImportAsync(stream, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogInformation(ex, "Invalid recipe import JSON file");
            return BadRequest(new ProblemDetails
            {
                Title = await _messages.GetAsync("recipe.invalidFileTitle", HttpContext.RequestAborted),
                Detail = await _messages.GetAsync("recipe.invalidFileDetail", HttpContext.RequestAborted)
            });
        }
        catch (RecipeExtractionFailedException ex)
        {
            _logger.LogInformation(ex, "Recipe import file contained no recipes");
            return UnprocessableEntity(new ProblemDetails
            {
                Title = await _messages.GetAsync("recipe.noRecipesTitle", HttpContext.RequestAborted),
                Detail = await _messages.GetAsync("recipe.noRecipesDetail", HttpContext.RequestAborted)
            });
        }
    }

    private static List<string> SplitTags(string? tags) => (tags ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();

}

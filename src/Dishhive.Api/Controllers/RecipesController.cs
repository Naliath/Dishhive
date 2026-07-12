using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services;
using Dishhive.Api.Services.Collections;
using Dishhive.Api.Services.Facts;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Localization;
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
    private readonly IRecipeExchangeService _exchangeService;
    private readonly RecipeFactsAssessmentService _factsQueue;
    private readonly ISafeHttpFetcher _httpFetcher;
    private readonly ILogger<RecipesController> _logger;
    private readonly UserMessageLocalizer _messages;

    public RecipesController(
        DishhiveDbContext context,
        IRecipeImportService importService,
        IRecipeExchangeService exchangeService,
        RecipeFactsAssessmentService factsQueue,
        ISafeHttpFetcher httpFetcher,
        UserMessageLocalizer messages,
        ILogger<RecipesController> logger)
    {
        _context = context;
        _importService = importService;
        _exchangeService = exchangeService;
        _factsQueue = factsQueue;
        _httpFetcher = httpFetcher;
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
        CancellationToken cancellationToken = default)
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

        var recipes = await RecipeListMapping.Project(
            query.OrderBy(r => r.Title), _context.CookbookEntries.AsNoTracking())
            .ToListAsync(cancellationToken);
        RecipeListMapping.ResolveLocalImageUrls(recipes);
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
        var recipe = await _context.Recipes.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (recipe == null)
        {
            return NotFound();
        }
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails { Title = await _messages.GetAsync("recipe.noImageTitle", cancellationToken) });
        }
        if (file.Length > RecipeImageProcessor.MaxSourceBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new ProblemDetails
            {
                Title = await _messages.GetAsync("recipe.imageTooLargeTitle", cancellationToken),
                Detail = await _messages.GetAsync("recipe.imageTooLargeDetail", cancellationToken,
                    ("maxMb", RecipeImageProcessor.MaxSourceBytes / 1024 / 1024))
            });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var processed = await RecipeImageProcessor.ProcessAsync(stream, cancellationToken);
            recipe.ImageData = processed.Data;
            recipe.ImageContentType = processed.ContentType;
            recipe.ImageUrl = null;
            await _context.SaveChangesAsync(cancellationToken);
            return NoContent();
        }
        catch (RecipeImageException ex)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = await _messages.GetAsync("recipe.invalidImageTitle", cancellationToken),
                Detail = ex.Message
            });
        }
    }

    /// <summary>Removes both the local recipe image and its remote source reference</summary>
    [HttpDelete("{id:guid}/image")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRecipeImage(Guid id, CancellationToken cancellationToken)
    {
        var recipe = await _context.Recipes.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (recipe == null)
        {
            return NotFound();
        }

        recipe.ImageData = null;
        recipe.ImageContentType = null;
        recipe.ImageUrl = null;
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
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
            .Include(r => r.DietaryFacts)
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
        if (!await _context.Recipes.AnyAsync(r => r.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var targetIds = dto.CookbookIds.Distinct().ToList();
        var knownIds = await _context.Cookbooks
            .Where(c => targetIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var unknown = targetIds.Except(knownIds).ToList();
        if (unknown.Count > 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = await _messages.GetAsync("recipe.unknownCollectionsTitle", cancellationToken),
                Detail = await _messages.GetAsync("recipe.unknownCollectionsDetail", cancellationToken,
                    ("ids", string.Join(", ", unknown)))
            });
        }

        var current = await _context.CookbookEntries
            .Where(e => e.RecipeId == id)
            .ToListAsync(cancellationToken);

        _context.CookbookEntries.RemoveRange(current.Where(e => !targetIds.Contains(e.CookbookId)));
        var existing = current.Select(e => e.CookbookId).ToHashSet();
        foreach (var cookbookId in targetIds.Where(cid => !existing.Contains(cid)))
        {
            _context.CookbookEntries.Add(new CookbookEntry { CookbookId = cookbookId, RecipeId = id });
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Ok(targetIds);
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
        if (dto.Tags.Any(t => t.Trim().Length > 50))
        {
            return await TagTooLongAsync(cancellationToken);
        }
        if (HasUnknownClass(dto.ContainsClasses))
        {
            return await UnknownClassAsync(cancellationToken);
        }

        var recipe = new Recipe();
        ApplyDto(recipe, dto);
        if (!string.IsNullOrWhiteSpace(dto.ImageUrl))
        {
            var imageResult = await TrySetImageFromUrlAsync(recipe, dto.ImageUrl, cancellationToken);
            if (imageResult != null)
            {
                return imageResult;
            }
        }
        if (dto.ContainsClasses != null)
        {
            ApplyFacts(recipe, ParseClasses(dto.ContainsClasses), DietaryFactsStatus.UserConfirmed);
        }

        _context.Recipes.Add(recipe);
        await SyncTagsAsync(recipe, dto.Tags);
        await _context.SaveChangesAsync(cancellationToken);
        if (dto.ContainsClasses == null)
        {
            _factsQueue.TryEnqueue(recipe.Id);
        }

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
    public async Task<ActionResult<RecipeDto>> UpdateRecipe(
        Guid id, UpdateRecipeDto dto, CancellationToken cancellationToken)
    {
        if (dto.Tags.Any(t => t.Trim().Length > 50))
        {
            return await TagTooLongAsync(cancellationToken);
        }
        if (HasUnknownClass(dto.ContainsClasses))
        {
            return await UnknownClassAsync(cancellationToken);
        }

        var recipe = await _context.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .Include(r => r.Tags).ThenInclude(a => a.RecipeTag)
            .Include(r => r.DietaryFacts)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (recipe == null)
        {
            return NotFound();
        }

        var requestedImageUrl = string.IsNullOrWhiteSpace(dto.ImageUrl) ? null : dto.ImageUrl.Trim();
        var isCurrentLocalEndpoint = string.Equals(
            requestedImageUrl, ImageEndpoint(recipe.Id), StringComparison.OrdinalIgnoreCase);
        if (requestedImageUrl != null
            && !isCurrentLocalEndpoint
            && (recipe.ImageData == null
                || !string.Equals(recipe.ImageUrl, requestedImageUrl, StringComparison.Ordinal)))
        {
            // Download before mutating the rest of the aggregate, so an invalid or
            // unreachable image URL cannot leave a half-applied recipe edit.
            var imageResult = await TrySetImageFromUrlAsync(recipe, requestedImageUrl, cancellationToken);
            if (imageResult != null)
            {
                return imageResult;
            }
        }

        var previousIngredients = recipe.Ingredients
            .Select(i => i.Name.Trim().ToLowerInvariant())
            .OrderBy(n => n)
            .ToList();

        _context.RecipeIngredients.RemoveRange(recipe.Ingredients);
        _context.RecipeSteps.RemoveRange(recipe.Steps);
        recipe.Ingredients.Clear();
        recipe.Steps.Clear();

        ApplyDto(recipe, dto);
        if (dto.ContainsClasses != null)
        {
            ApplyFacts(recipe, ParseClasses(dto.ContainsClasses), DietaryFactsStatus.UserConfirmed);
        }
        await SyncTagsAsync(recipe, dto.Tags);
        await _context.SaveChangesAsync(cancellationToken);
        await RemoveOrphanedTagsAsync();

        // A changed ingredient list makes stored facts stale — even user-confirmed
        // ones describe ingredients that no longer exist — so re-assess (unless the
        // same request set the facts explicitly, which is the freshest verdict).
        var newIngredients = recipe.Ingredients
            .Select(i => i.Name.Trim().ToLowerInvariant())
            .OrderBy(n => n)
            .ToList();
        if (dto.ContainsClasses == null && !previousIngredients.SequenceEqual(newIngredients))
        {
            _factsQueue.TryEnqueue(recipe.Id, overwriteUserConfirmed: true);
        }

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

        var cookbookEntries = await _context.CookbookEntries
            .Where(e => e.RecipeId == id)
            .ToListAsync();
        _context.CookbookEntries.RemoveRange(cookbookEntries);

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
        if (HasUnknownClass(dto.Contains))
        {
            return await UnknownClassAsync(HttpContext.RequestAborted);
        }

        var recipe = await _context.Recipes
            .Include(r => r.DietaryFacts)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (recipe == null)
        {
            return NotFound();
        }

        ApplyFacts(recipe, ParseClasses(dto.Contains), DietaryFactsStatus.UserConfirmed);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User confirmed dietary facts for {Title}: [{Classes}]",
            recipe.Title, string.Join(", ", recipe.DietaryFacts.Select(f => f.IngredientClass)));
        return Ok(FactsDto(recipe));
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
            QueueDepth = queue.QueueDepth,
            Running = queue.Running,
            Available = extractor.IsAvailable,
            LastError = queue.LastError
        });
    }

    /// <summary>
    /// Queues every still-unassessed recipe for AI facts assessment (the settings
    /// page backfill). Already-assessed recipes are never re-queued here — re-runs
    /// happen per recipe via ingredient edits or the explicit facts editor.
    /// </summary>
    [HttpPost("facts/backfill")]
    [ProducesResponseType(typeof(RecipeFactsBackfillResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RecipeFactsBackfillResultDto>> BackfillFacts(CancellationToken cancellationToken)
    {
        var unassessedIds = await _context.Recipes
            .Where(r => r.DietaryFactsStatus == DietaryFactsStatus.Unassessed)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var enqueued = _factsQueue.EnqueueMany(unassessedIds);
        _logger.LogInformation("Facts backfill requested: {Enqueued} of {Total} unassessed recipes queued",
            enqueued, unassessedIds.Count);
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

    private async Task<BadRequestObjectResult> TagTooLongAsync(CancellationToken cancellationToken) => BadRequest(new ProblemDetails
    {
        Title = await _messages.GetAsync("recipe.tagTooLongTitle", cancellationToken),
        Detail = await _messages.GetAsync("recipe.tagTooLongDetail", cancellationToken)
    });

    private async Task<BadRequestObjectResult> UnknownClassAsync(CancellationToken cancellationToken) => BadRequest(new ProblemDetails
    {
        Title = await _messages.GetAsync("recipe.unknownClassTitle", cancellationToken),
        Detail = await _messages.GetAsync("recipe.containedClassDetail", cancellationToken)
    });

    private static bool HasUnknownClass(List<string>? names) =>
        names != null && names.Any(n => !IngredientClasses.TryParse(n, out _));

    /// <summary>Distinct parsed classes; class names were validated up front</summary>
    private static List<IngredientClass> ParseClasses(List<string> names) => names
        .Select(n => IngredientClasses.TryParse(n, out var value) ? value : (IngredientClass?)null)
        .Where(v => v.HasValue)
        .Select(v => v!.Value)
        .Distinct()
        .ToList();

    /// <summary>Replaces the facts rows and stamps the assessment status</summary>
    private void ApplyFacts(Recipe recipe, List<IngredientClass> classes, DietaryFactsStatus status)
    {
        _context.RecipeDietaryFacts.RemoveRange(recipe.DietaryFacts);
        recipe.DietaryFacts.Clear();
        foreach (var ingredientClass in classes)
        {
            recipe.DietaryFacts.Add(new RecipeDietaryFact { Recipe = recipe, IngredientClass = ingredientClass });
        }
        recipe.DietaryFactsStatus = status;
        recipe.DietaryFactsAssessedAt = DateTime.UtcNow;
    }

    private static RecipeDietaryFactsDto FactsDto(Recipe recipe) => new()
    {
        Contains = IngredientClasses.ToNames(recipe.DietaryFacts.Select(f => f.IngredientClass)),
        Status = recipe.DietaryFactsStatus,
        AssessedAt = recipe.DietaryFactsAssessedAt
    };

    private static void ApplyDto(Recipe recipe, CreateRecipeDto dto)
    {
        recipe.Title = dto.Title;
        recipe.OriginalTitle = dto.OriginalTitle;
        recipe.Description = dto.Description;
        recipe.OriginalDescription = dto.OriginalDescription;
        recipe.ContentLanguage = dto.ContentLanguage;
        recipe.Servings = dto.Servings;
        recipe.PrepTimeMinutes = dto.PrepTimeMinutes;
        recipe.CookTimeMinutes = dto.CookTimeMinutes;
        recipe.TotalTimeMinutes = dto.TotalTimeMinutes;
        recipe.Category = dto.Category;
        recipe.Keywords = dto.Keywords;
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
                Instruction = step.Instruction,
                OriginalInstruction = step.OriginalInstruction
            });
        }
    }

    private static RecipeDto ToDto(Recipe recipe) => new()
    {
        Id = recipe.Id,
        Title = recipe.Title,
        OriginalTitle = recipe.OriginalTitle,
        Description = recipe.Description,
        OriginalDescription = recipe.OriginalDescription,
        ContentLanguage = recipe.ContentLanguage,
        Servings = recipe.Servings,
        PrepTimeMinutes = recipe.PrepTimeMinutes,
        CookTimeMinutes = recipe.CookTimeMinutes,
        TotalTimeMinutes = recipe.TotalTimeMinutes,
        Category = recipe.Category,
        Keywords = recipe.Keywords,
        // Remote URLs are retained as references only; clients always render the
        // Dishhive endpoint so recipe views never depend on an external host.
        ImageUrl = recipe.ImageData != null ? ImageEndpoint(recipe.Id) : null,
        HasLocalImage = recipe.ImageData != null,
        ImageSourceUrl = recipe.ImageUrl,
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
                Instruction = s.Instruction,
                OriginalInstruction = s.OriginalInstruction
            })
            .ToList(),
        Tags = recipe.Tags
            .Where(a => a.RecipeTag != null)
            .Select(a => a.RecipeTag!.Name)
            .OrderBy(n => n)
            .ToList(),
        DietaryFacts = FactsDto(recipe)
    };

    private async Task<UnprocessableEntityObjectResult?> TrySetImageFromUrlAsync(
        Recipe recipe, string imageUrl, CancellationToken cancellationToken)
    {
        var (ok, imageUri, error) = await UrlGuard.ValidateAsync(imageUrl.Trim(), cancellationToken);
        if (!ok || imageUri == null)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = await _messages.GetAsync("recipe.invalidImageUrlTitle", cancellationToken),
                Detail = error
            });
        }

        try
        {
            var processed = await RecipeImageDownloader.DownloadAsync(_httpFetcher, imageUri, cancellationToken);
            recipe.ImageData = processed.Data;
            recipe.ImageContentType = processed.ContentType;
            recipe.ImageUrl = imageUri.AbsoluteUri;
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or RecipeImageException)
        {
            _logger.LogWarning(ex, "Could not store recipe image from {Url}", imageUri);
            return UnprocessableEntity(new ProblemDetails
            {
                Title = await _messages.GetAsync("recipe.downloadImageTitle", cancellationToken),
                Detail = ex is RecipeImageException ? ex.Message : await _messages.GetAsync(
                    "recipe.downloadImageDetail", cancellationToken)
            });
        }
    }
}

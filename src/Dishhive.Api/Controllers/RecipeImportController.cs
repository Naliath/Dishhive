using Dishhive.Api.Data;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Models.Recipes;
using Dishhive.Api.Services.RecipeImport;
using Microsoft.AspNetCore.Mvc;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/recipe-import")]
[Produces("application/json")]
public class RecipeImportController : ControllerBase
{
    private readonly IRecipeImportService _import;
    private readonly DishhiveDbContext _db;

    public RecipeImportController(IRecipeImportService import, DishhiveDbContext db)
    {
        _import = import;
        _db = db;
    }

    /// <summary>
    /// Fetches and parses an external recipe URL. Returns a previewed recipe; the caller
    /// then submits it (possibly edited) to <c>POST /api/recipe-import/save</c> to persist it.
    /// </summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(ImportPreviewResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ImportPreviewResultDto>> Preview(ImportPreviewRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
            return BadRequest(new { message = "A valid absolute URL is required." });

        try
        {
            var result = await _import.PreviewAsync(uri, ct);
            return Ok(new ImportPreviewResultDto(
                result.Recipe.ToDto(),
                result.UsedAgent,
                result.BlueprintLearned,
                result.AgentNote));
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Persists a previewed (and possibly user-edited) imported recipe as a <see cref="Recipe"/>.
    /// Source-specific raw payload is preserved on the recipe for traceability.
    /// </summary>
    [HttpPost("save")]
    [ProducesResponseType(typeof(RecipeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RecipeDto>> Save(ImportedRecipeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { message = "Title is required." });

        var recipe = new Recipe
        {
            Title = dto.Title,
            Description = dto.Description,
            Servings = dto.Servings <= 0 ? 4 : dto.Servings,
            ImageUrl = dto.ImageUrl,
            VideoUrl = dto.VideoUrl,
            SourceUrl = dto.SourceUrl,
            SourceProviderKey = dto.ProviderKey,
            SourceRawPayload = dto.SourceRawPayload,
        };

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
            recipe.Steps.Add(new RecipeStep { Order = s.Order, Text = s.Text });
        foreach (var t in dto.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            recipe.Tags.Add(new RecipeTag { Tag = t });

        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        return CreatedAtAction(
            actionName: "Get",
            controllerName: "Recipes",
            routeValues: new { id = recipe.Id },
            value: recipe.ToDto());
    }

    [HttpGet("providers")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> Providers() => Ok(_import.SupportedProviderKeys);
}

using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Ai;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Suggestions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using static Dishhive.Api.Services.Recipes.RecipeDtoMapper;

namespace Dishhive.Api.Controllers;

public partial class RecipesController
{
    [HttpGet("import/capabilities")]
    public RecipeImportCapabilitiesDto GetImportCapabilities([FromServices] IRecipeContentExtractor extractor,
        [FromServices] IRecipeImageCapability images, [FromServices] AiOptions options) =>
        new(extractor.IsAvailable, images.State, options.ImportMaxTextCharacters, RecipeImageProcessor.MaxSourceBytes);

    [HttpPost("import/image-capability")]
    [EnableRateLimiting("expensive")]
    public async Task<RecipeImportCapabilitiesDto> CheckImportImageCapability([FromServices] IRecipeContentExtractor extractor,
        [FromServices] IRecipeImageCapability images, [FromServices] AiOptions options, CancellationToken cancellationToken)
    {
        await images.CheckAsync(cancellationToken, force: true);
        return GetImportCapabilities(extractor, images, options);
    }

    [HttpPost("import/text")]
    [EnableRateLimiting("expensive")]
    [RequestSizeLimit(1024 * 1024)]
    [ProducesResponseType(typeof(RecipeDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<RecipeDto>> ImportRecipeText(ImportRecipeTextRequestDto dto,
        [FromServices] IRecipeContentExtractor extractor, CancellationToken cancellationToken)
    {
        if (!extractor.IsAvailable) throw ImportProblems.Create("aiUnavailable", 503);
        var operationId = $"import-{Guid.NewGuid():N}";
        var imported = await extractor.ExtractAsync(dto.Text, null, operationId, cancellationToken);
        var recipe = await _importService.ImportExtractedAsync(imported, RecipeImportSource.Text, operationId, cancellationToken);
        return CreatedAtAction(nameof(GetRecipe), new { id = recipe.Id }, ToDto(recipe));
    }

    [HttpPost("import/image")]
    [EnableRateLimiting("expensive")]
    [RequestSizeLimit(RecipeImageProcessor.MaxSourceBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = RecipeImageProcessor.MaxSourceBytes + 65536)]
    [ProducesResponseType(typeof(RecipeDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<RecipeDto>> ImportRecipeImage(IFormFile? file,
        [FromServices] IRecipeContentExtractor extractor, [FromServices] IRecipeImageCapability images,
        CancellationToken cancellationToken)
    {
        if (!extractor.IsAvailable) throw ImportProblems.Create("aiUnavailable", 503);
        if (file is null || file.Length == 0) throw ImportProblems.Create("invalidImage", 400);
        if (file.Length > RecipeImageProcessor.MaxSourceBytes) throw ImportProblems.Create("inputTooLarge", 413);
        ProcessedRecipeImage image;
        try
        {
            await using var stream = file.OpenReadStream();
            image = await RecipeImageProcessor.ProcessAsync(stream, 2048, 90, cancellationToken);
        }
        catch (RecipeImageException) { throw ImportProblems.Create("invalidImage"); }
        var state = await images.CheckAsync(cancellationToken);
        if (state != "available") throw ImportProblems.Create(state == "unsupported" ? "visionUnsupported" : "visionUnavailable", 503);
        var operationId = $"import-{Guid.NewGuid():N}";
        var imported = await extractor.ExtractAsync(null, image, operationId, cancellationToken);
        var recipe = await _importService.ImportExtractedAsync(imported, RecipeImportSource.Photo, operationId, cancellationToken);
        return CreatedAtAction(nameof(GetRecipe), new { id = recipe.Id }, ToDto(recipe));
    }
}

using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Api;
using Dishhive.Api.Services.Ai;
using Dishhive.Api.Services.Facts;
using Dishhive.Api.Services.Import;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Recipes;

/// <summary>Owns recipe writes, revision invalidation and durable facts work as one application boundary.</summary>
public sealed class RecipeMutationService(
    DishhiveDbContext _context, RecipeFactsAssessmentService _factsQueue,
    ISafeHttpFetcher _httpFetcher, ILogger<RecipeMutationService> _logger, RecipeReviewTelemetry _reviews)
{
    public async Task SetRecipeImage(
        Guid id, Stream? stream, long length, CancellationToken cancellationToken)
    {
        var recipe = await _context.Recipes.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (recipe == null)
        {
            throw Missing(id);
        }
        if (stream == null || length == 0)
        {
            throw new ApiProblemException(400, "image_required", "recipe.noImageTitle");
        }
        if (length > RecipeImageProcessor.MaxSourceBytes)
        {
            throw new ApiProblemException(413, "image_too_large", "recipe.imageTooLargeTitle", "recipe.imageTooLargeDetail", parameters: [("maxMb", RecipeImageProcessor.MaxSourceBytes / 1024 / 1024)]);
        }

        try
        {
            var processed = await RecipeImageProcessor.ProcessAsync(stream, cancellationToken);
            recipe.ImageData = processed.Data;
            recipe.ImageContentType = processed.ContentType;
            recipe.ImageUrl = null;
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }
        catch (RecipeImageException ex)
        {
            throw new ApiProblemException(422, "invalid_image", "recipe.invalidImageTitle", detail: ex.Message);
        }
    }

    public async Task DeleteRecipeImage(Guid id, CancellationToken cancellationToken)
    {
        var recipe = await _context.Recipes.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (recipe == null)
        {
            throw Missing(id);
        }

        recipe.ImageData = null;
        recipe.ImageContentType = null;
        recipe.ImageUrl = null;
        await _context.SaveChangesAsync(cancellationToken);
        return;
    }

    public async Task<List<Guid>> SetRecipeCookbooks(
        Guid id, RecipeCookbooksRequestDto dto, CancellationToken cancellationToken)
    {
        if (!await _context.Recipes.AnyAsync(r => r.Id == id, cancellationToken))
        {
            throw Missing(id);
        }

        var targetIds = dto.CookbookIds.Distinct().ToList();
        var knownIds = await _context.Cookbooks
            .Where(c => targetIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var unknown = targetIds.Except(knownIds).ToList();
        if (unknown.Count > 0)
        {
            throw new ApiProblemException(400, "unknown_collections", "recipe.unknownCollectionsTitle", "recipe.unknownCollectionsDetail", parameters: [("ids", string.Join(", ", unknown))]);
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
        return targetIds;
    }

    public async Task<Recipe> CreateRecipe(
        CreateRecipeDto dto, CancellationToken cancellationToken)
    {
        if (dto.Tags.Any(t => t.Trim().Length > 50))
        {
            throw new ApiProblemException(400, "tag_too_long", "recipe.tagTooLongTitle", "recipe.tagTooLongDetail");
        }
        if (HasUnknownClass(dto.ContainsClasses))
        {
            throw UnknownClass();
        }

        var recipe = new Recipe();
        ApplyDto(recipe, dto);
        if (!string.IsNullOrWhiteSpace(dto.ImageUrl))
        {
            await SetImageFromUrlAsync(recipe, dto.ImageUrl, cancellationToken);
        }
        if (dto.ContainsClasses != null)
        {
            RecipeFactsStateService.Replace(_context, recipe, ParseClasses(dto.ContainsClasses), DietaryFactsStatus.UserConfirmed);
        }

        _context.Recipes.Add(recipe);
        await SyncTagsAsync(recipe, dto.Tags);
        if (dto.ContainsClasses == null)
        {
            await _factsQueue.StageAsync(_context, recipe, cancellationToken: cancellationToken);
        }
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created recipe {Title} ({Id})", recipe.Title, recipe.Id);
        return recipe;
    }

    public async Task<Recipe> UpdateRecipe(
        Guid id, UpdateRecipeDto dto, CancellationToken cancellationToken)
    {
        if (dto.Tags.Any(t => t.Trim().Length > 50))
        {
            throw new ApiProblemException(400, "tag_too_long", "recipe.tagTooLongTitle", "recipe.tagTooLongDetail");
        }
        if (HasUnknownClass(dto.ContainsClasses))
        {
            throw UnknownClass();
        }

        var recipe = await _context.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .Include(r => r.Tags).ThenInclude(a => a.RecipeTag)
            .Include(r => r.DietaryFacts)
            .Include(r => r.DietaryFactsHistory)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (recipe == null)
        {
            throw Missing(id);
        }

        var review = RecipeReviewTelemetry.Capture(recipe);
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
            await SetImageFromUrlAsync(recipe, requestedImageUrl, cancellationToken);
        }

        var previousTitle = recipe.Title;
        var previousDescription = recipe.Description;
        var previousAmounts = recipe.Ingredients.OrderBy(item => item.SortOrder)
            .Select(item => (item.Name, item.Quantity, item.Unit)).ToList();
        var previousTiming = (recipe.Servings, recipe.PrepTimeMinutes, recipe.CookTimeMinutes, recipe.TotalTimeMinutes);
        var previousIngredients = recipe.Ingredients
            .Select(i => i.Name.Trim().ToLowerInvariant())
            .OrderBy(n => n)
            .ToList();
        var previousSteps = recipe.Steps.OrderBy(step => step.StepNumber)
            .Select(step => step.Instruction.Trim()).ToList();

        _context.RecipeIngredients.RemoveRange(recipe.Ingredients);
        _context.RecipeSteps.RemoveRange(recipe.Steps);
        recipe.Ingredients.Clear();
        recipe.Steps.Clear();

        ApplyDto(recipe, dto);
        var newIngredients = recipe.Ingredients
            .Select(i => i.Name.Trim().ToLowerInvariant())
            .OrderBy(n => n)
            .ToList();
        var newSteps = recipe.Steps.OrderBy(step => step.StepNumber)
            .Select(step => step.Instruction.Trim()).ToList();
        var factsSourceChanged = !string.Equals(previousTitle, recipe.Title, StringComparison.Ordinal)
            || !previousIngredients.SequenceEqual(newIngredients);
        var sourceChanged = factsSourceChanged
            || !string.Equals(previousDescription, recipe.Description, StringComparison.Ordinal)
            || !previousSteps.SequenceEqual(newSteps)
            || !previousAmounts.SequenceEqual(recipe.Ingredients.OrderBy(item => item.SortOrder)
                .Select(item => (item.Name, item.Quantity, item.Unit)))
            || previousTiming != (recipe.Servings, recipe.PrepTimeMinutes, recipe.CookTimeMinutes, recipe.TotalTimeMinutes);
        if (sourceChanged)
        {
            recipe.Revision++;
            if (!factsSourceChanged && recipe.DietaryFactsAssessedRevision.HasValue)
                recipe.DietaryFactsAssessedRevision = recipe.Revision;
        }
        if (dto.ContainsClasses != null)
        {
            RecipeFactsStateService.Replace(_context, recipe, ParseClasses(dto.ContainsClasses), DietaryFactsStatus.UserConfirmed);
        }
        else if (factsSourceChanged)
        {
            RecipeFactsStateService.Invalidate(recipe, Math.Max(1, recipe.Revision - 1));
        }
        var needsFactsAssessment = dto.ContainsClasses == null
            && (factsSourceChanged || (sourceChanged && recipe.DietaryFactsAssessedRevision is null));
        await SyncTagsAsync(recipe, dto.Tags);

        // A changed ingredient list makes stored facts stale — even user-confirmed
        // ones describe ingredients that no longer exist — so re-assess (unless the
        // same request set the facts explicitly, which is the freshest verdict).
        if (needsFactsAssessment)
        {
            var queued = await _factsQueue.StageAsync(_context, recipe, overwriteUserConfirmed: true,
                cancellationToken: cancellationToken);
            if (queued) recipe.DietaryFactsStatus = DietaryFactsStatus.Pending;
        }
        await _reviews.StageAsync(recipe, review, dto.ContainsClasses != null, cancellationToken);
        await SaveAndPruneAsync(cancellationToken);

        return recipe;
    }

    public async Task DeleteRecipe(Guid id, CancellationToken cancellationToken)
    {
        // Children and planner references are handled client-side (not via database
        // cascade) so delete semantics are explicit and identical across providers
        var recipe = await _context.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe == null)
        {
            throw Missing(id);
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
        await SaveAndPruneAsync(cancellationToken);

        _logger.LogInformation("Deleted recipe {Id}", id);
        return;
    }

    public async Task<Recipe> SetRecipeFacts(Guid id, UpdateRecipeFactsDto dto, CancellationToken cancellationToken)
    {
        if (HasUnknownClass(dto.Contains))
        {
            throw UnknownClass();
        }

        var recipe = await _context.Recipes
            .Include(r => r.DietaryFacts)
            .Include(r => r.DietaryFactsHistory)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (recipe == null)
        {
            throw Missing(id);
        }

        var review = RecipeReviewTelemetry.Capture(recipe);
        RecipeFactsStateService.Replace(_context, recipe, ParseClasses(dto.Contains), DietaryFactsStatus.UserConfirmed);
        await _reviews.StageAsync(recipe, review, true, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User confirmed dietary facts for {Title}: [{Classes}]",
            recipe.Title, string.Join(", ", recipe.DietaryFacts.Select(f => f.IngredientClass)));
        return recipe;
    }

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

    private async Task SaveAndPruneAsync(CancellationToken token)
    {
        // InMemory has no transaction support; relational writes and orphan cleanup commit together.
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(token) : null;
        await _context.SaveChangesAsync(token);
        await RemoveOrphanedTagsAsync();
        if (transaction != null) await transaction.CommitAsync(token);
    }

    private static ApiProblemException Missing(Guid id) => new(404, "recipe_missing",
        "common.unknownRecipeTitle", "common.unknownRecipeDetail", parameters: [("recipeId", id)]);
    private static ApiProblemException UnknownClass() => new(400, "unknown_ingredient_class",
        "recipe.unknownClassTitle", "recipe.containedClassDetail");
    private static bool HasUnknownClass(List<string>? names) =>
        names != null && names.Any(n => !IngredientClasses.TryParse(n, out _));
    private static List<IngredientClass> ParseClasses(List<string> names) => names
        .Select(n => IngredientClasses.TryParse(n, out var value) ? value : (IngredientClass?)null)
        .Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
    private static string ImageEndpoint(Guid id) => $"/api/recipes/{id}/image";

    private async Task SetImageFromUrlAsync(Recipe recipe, string imageUrl, CancellationToken cancellationToken)
    {
        var (ok, imageUri, error) = await UrlGuard.ValidateAsync(imageUrl.Trim(), cancellationToken);
        if (!ok || imageUri == null)
            throw new ApiProblemException(422, "invalid_image_url", "recipe.invalidImageUrlTitle", detail: error);
        try
        {
            var processed = await RecipeImageDownloader.DownloadAsync(_httpFetcher, imageUri, cancellationToken);
            recipe.ImageData = processed.Data;
            recipe.ImageContentType = processed.ContentType;
            recipe.ImageUrl = imageUri.AbsoluteUri;
        }
        catch (Exception ex) when (ex is HttpRequestException or RecipeImageException
            || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not store recipe image from {Url}", imageUri);
            throw new ApiProblemException(422, "image_download_failed", "recipe.downloadImageTitle",
                ex is RecipeImageException ? null : "recipe.downloadImageDetail",
                detail: ex is RecipeImageException ? ex.Message : null);
        }
    }
}

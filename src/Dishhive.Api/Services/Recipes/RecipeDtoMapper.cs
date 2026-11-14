using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Import;

namespace Dishhive.Api.Services.Recipes;

public static class RecipeDtoMapper
{
    public static RecipeDietaryFactsDto FactsDto(Recipe recipe) => new()
    {
        Contains = IngredientClasses.ToNames(recipe.DietaryFacts.Select(f => f.IngredientClass)),
        Status = recipe.DietaryFactsStatus,
        AssessedAt = recipe.DietaryFactsAssessedAt,
        AssessedRevision = recipe.DietaryFactsAssessedRevision,
        RecipeRevision = recipe.Revision,
        History = recipe.DietaryFactsHistory
            .OrderByDescending(h => h.ArchivedAt)
            .Select(h => new RecipeDietaryFactsHistoryDto(
                h.RecipeRevision,
                h.Status,
                h.Contains.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                h.AssessedAt,
                h.ArchivedAt))
            .ToList()
    };
    public static RecipeDto ToDto(Recipe recipe) => new()
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
        ImageUrl = recipe.ImageData != null ? $"/api/recipes/{recipe.Id}/image?v={recipe.UpdatedAt.Ticks}" : null,
        HasLocalImage = recipe.ImageData != null,
        ImageSourceUrl = recipe.ImageUrl,
        VideoUrl = recipe.VideoUrl,
        SourceUrl = recipe.SourceUrl,
        SourceProvider = recipe.SourceProvider,
        CreatedAt = recipe.CreatedAt,
        UpdatedAt = recipe.UpdatedAt,
        Ingredients = recipe.Ingredients
            .OrderBy(i => i.SortOrder)
            .Select(i => (Ingredient: i, Measure: IngredientMeasurementProjection.Resolve(i, recipe.SourceRawData)))
            .Select(item => new RecipeIngredientDto
            {
                Id = item.Ingredient.Id,
                SortOrder = item.Ingredient.SortOrder,
                Name = item.Measure.Name,
                Quantity = item.Measure.Quantity,
                Unit = item.Measure.Unit,
                OriginalText = item.Ingredient.OriginalText,
                OriginalQuantity = item.Measure.OriginalQuantity,
                OriginalUnit = item.Measure.OriginalUnit
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
}

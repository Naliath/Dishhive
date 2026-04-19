using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.DTOs;

public static class RecipeDtos
{
    public record RecipeDto(
        Guid Id,
        string Title,
        string? Description,
        int Servings,
        int? PrepTimeMinutes,
        int? CookTimeMinutes,
        string? PictureUrl,
        string? VideoUrl,
        string? SourceUrl,
        string? SourceName,
        List<string> Tags,
        List<RecipeIngredientDto> Ingredients,
        List<RecipeStepDto> Steps,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );

    public record RecipeIngredientDto(
        Guid Id,
        string Name,
        decimal? Quantity,
        string? Unit,
        decimal? OriginalQuantity,
        string? OriginalUnit,
        string? Notes,
        int SortOrder
    );

    public record RecipeStepDto(
        Guid Id,
        int StepNumber,
        string Instruction
    );

    public record RecipeSummaryDto(
        Guid Id,
        string Title,
        string? Description,
        int Servings,
        string? PictureUrl,
        List<string> Tags,
        DateTime CreatedAt
    );

    public record CreateRecipeDto(
        [Required][MaxLength(200)] string Title,
        [MaxLength(2000)] string? Description,
        int Servings = 4,
        int? PrepTimeMinutes = null,
        int? CookTimeMinutes = null,
        [MaxLength(500)] string? PictureUrl = null,
        [MaxLength(500)] string? VideoUrl = null,
        List<string>? Tags = null,
        List<CreateRecipeIngredientDto>? Ingredients = null,
        List<CreateRecipeStepDto>? Steps = null
    );

    public record CreateRecipeIngredientDto(
        [Required][MaxLength(200)] string Name,
        decimal? Quantity,
        [MaxLength(50)] string? Unit,
        decimal? OriginalQuantity,
        [MaxLength(50)] string? OriginalUnit,
        [MaxLength(500)] string? Notes,
        int SortOrder = 0
    );

    public record CreateRecipeStepDto(
        int StepNumber,
        [Required][MaxLength(2000)] string Instruction
    );

    public record UpdateRecipeDto(
        [Required][MaxLength(200)] string Title,
        [MaxLength(2000)] string? Description,
        int Servings,
        int? PrepTimeMinutes,
        int? CookTimeMinutes,
        [MaxLength(500)] string? PictureUrl,
        [MaxLength(500)] string? VideoUrl,
        List<string>? Tags,
        List<CreateRecipeIngredientDto>? Ingredients,
        List<CreateRecipeStepDto>? Steps
    );

    public record ImportRecipeRequestDto([Required] string Url);
}

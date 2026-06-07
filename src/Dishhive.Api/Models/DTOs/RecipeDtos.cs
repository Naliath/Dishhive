using System.ComponentModel.DataAnnotations;
using Dishhive.Api.Models.Recipes;

namespace Dishhive.Api.Models.DTOs;

public sealed record RecipeDto(
    Guid Id,
    string Title,
    string? Description,
    int Servings,
    string? ImageUrl,
    string? VideoUrl,
    string? SourceUrl,
    string? SourceProviderKey,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<RecipeIngredientDto> Ingredients,
    IReadOnlyList<RecipeStepDto> Steps,
    IReadOnlyList<string> Tags);

public sealed record RecipeSummaryDto(
    Guid Id,
    string Title,
    string? Description,
    int Servings,
    string? ImageUrl,
    IReadOnlyList<string> Tags);

public sealed record RecipeIngredientDto(
    Guid Id,
    int Order,
    string Name,
    decimal? Quantity,
    string? Unit,
    decimal? OriginalQuantity,
    string? OriginalUnit,
    string? Section,
    string? Note);

public sealed record RecipeStepDto(Guid Id, int Order, string Text);

public sealed class CreateRecipeIngredientDto
{
    public int Order { get; set; }
    [Required, MaxLength(300)] public string Name { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    [MaxLength(50)] public string? Unit { get; set; }
    public decimal? OriginalQuantity { get; set; }
    [MaxLength(50)] public string? OriginalUnit { get; set; }
    [MaxLength(100)] public string? Section { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
}

public sealed class CreateRecipeStepDto
{
    public int Order { get; set; }
    [Required] public string Text { get; set; } = string.Empty;
}

public class CreateRecipeDto
{
    [Required, MaxLength(300)] public string Title { get; set; } = string.Empty;
    [MaxLength(2000)] public string? Description { get; set; }
    [Range(1, 99)] public int Servings { get; set; } = 4;
    [MaxLength(1000)] public string? ImageUrl { get; set; }
    [MaxLength(1000)] public string? VideoUrl { get; set; }
    [MaxLength(1000)] public string? SourceUrl { get; set; }
    [MaxLength(100)] public string? SourceProviderKey { get; set; }
    public string? SourceRawPayload { get; set; }
    [MaxLength(2000)] public string? Notes { get; set; }
    public List<CreateRecipeIngredientDto> Ingredients { get; set; } = new();
    public List<CreateRecipeStepDto> Steps { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}

public sealed class UpdateRecipeDto : CreateRecipeDto { }

public static class RecipeMappers
{
    public static RecipeDto ToDto(this Recipe r) => new(
        r.Id, r.Title, r.Description, r.Servings, r.ImageUrl, r.VideoUrl, r.SourceUrl,
        r.SourceProviderKey, r.Notes, r.CreatedAt, r.UpdatedAt,
        r.Ingredients.OrderBy(i => i.Order).Select(ToDto).ToList(),
        r.Steps.OrderBy(s => s.Order).Select(ToDto).ToList(),
        r.Tags.Select(t => t.Tag).ToList());

    public static RecipeSummaryDto ToSummary(this Recipe r) => new(
        r.Id, r.Title, r.Description, r.Servings, r.ImageUrl,
        r.Tags.Select(t => t.Tag).ToList());

    public static RecipeIngredientDto ToDto(this RecipeIngredient i) =>
        new(i.Id, i.Order, i.Name, i.Quantity, i.Unit, i.OriginalQuantity, i.OriginalUnit, i.Section, i.Note);

    public static RecipeStepDto ToDto(this RecipeStep s) => new(s.Id, s.Order, s.Text);
}

using Dishhive.Api.Services.RecipeImport;

namespace Dishhive.Api.Models.DTOs;

public sealed record ImportPreviewRequest(string Url);

/// <summary>
/// Wraps a previewed recipe with metadata about how it was obtained:
/// whether the LLM agent was used, and whether a reusable parsing blueprint
/// was learned for the source's host.
/// </summary>
public sealed record ImportPreviewResultDto(
    ImportedRecipeDto Recipe,
    bool UsedAgent,
    bool BlueprintLearned,
    string? AgentNote);

public sealed record ImportedRecipeDto(
    string Title,
    string? Description,
    int Servings,
    string? ImageUrl,
    string? VideoUrl,
    string SourceUrl,
    string ProviderKey,
    string SourceRawPayload,
    IReadOnlyList<ImportedIngredientDto> Ingredients,
    IReadOnlyList<ImportedStepDto> Steps,
    IReadOnlyList<string> Tags);

public sealed record ImportedIngredientDto(
    int Order,
    string Name,
    decimal? Quantity,
    string? Unit,
    decimal? OriginalQuantity,
    string? OriginalUnit,
    string? Section,
    string? Note);

public sealed record ImportedStepDto(int Order, string Text);

public static class ImportedRecipeDtoMapper
{
    public static ImportedRecipeDto ToDto(this ImportedRecipe r) => new(
        r.Title,
        r.Description,
        r.Servings,
        r.ImageUrl,
        r.VideoUrl,
        r.SourceUrl.ToString(),
        r.ProviderKey,
        r.SourceRawPayload,
        r.Ingredients.Select(i => new ImportedIngredientDto(
            i.Order, i.Name, i.Quantity, i.Unit, i.OriginalQuantity, i.OriginalUnit, i.Section, i.Note)).ToList(),
        r.Steps.Select(s => new ImportedStepDto(s.Order, s.Text)).ToList(),
        r.Tags);
}

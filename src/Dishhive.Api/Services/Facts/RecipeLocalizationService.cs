using Dishhive.Api.Models;
using Dishhive.Api.Services.Suggestions;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Dishhive.Api.Services.Facts;

public sealed record LocalizedRecipeText(
    string Title, string? Description, List<string> IngredientNames, List<string> Steps);

public interface IRecipeLocalizationService
{
    bool IsAvailable { get; }
    Task<LocalizedRecipeText?> TranslateAsync(
        Recipe recipe, string targetLanguage, CancellationToken cancellationToken = default);
}

public sealed class NoOpRecipeLocalizationService : IRecipeLocalizationService
{
    public bool IsAvailable => false;
    public Task<LocalizedRecipeText?> TranslateAsync(
        Recipe recipe, string targetLanguage, CancellationToken cancellationToken = default) =>
        Task.FromResult<LocalizedRecipeText?>(null);
}

public sealed class LlmRecipeLocalizationService(
    IChatClient chatClient,
    AiOptions options,
    ILogger<LlmRecipeLocalizationService> logger) : IRecipeLocalizationService
{
    public bool IsAvailable => true;

    public async Task<LocalizedRecipeText?> TranslateAsync(
        Recipe recipe, string targetLanguage, CancellationToken cancellationToken = default)
    {
        var languageName = targetLanguage == "nl" ? "Dutch" : "English";
        var source = JsonSerializer.Serialize(new
        {
            recipe.Title,
            recipe.Description,
            ingredients = recipe.Ingredients.OrderBy(item => item.SortOrder).Select(item => item.Name),
            steps = recipe.Steps.OrderBy(item => item.StepNumber).Select(item => item.Instruction)
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            var response = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System,
                        $"Translate recipe text from any language to {languageName}. Preserve culinary meaning, quantities, names and array lengths. Reply only JSON: {{\"title\":\"\",\"description\":null,\"ingredientNames\":[],\"steps\":[]}}."),
                    new ChatMessage(ChatRole.User, source)
                ],
                new ChatOptions { Temperature = 0, MaxOutputTokens = options.MaxOutputTokens },
                cancellationToken: timeout.Token);
            var text = response.Text ?? "";
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            var translated = JsonSerializer.Deserialize<LocalizedRecipeText>(
                text[start..(end + 1)], new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (translated == null
                || translated.IngredientNames.Count != recipe.Ingredients.Count
                || translated.Steps.Count != recipe.Steps.Count
                || string.IsNullOrWhiteSpace(translated.Title)) return null;
            return translated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Recipe translation failed for {RecipeId}", recipe.Id);
            return null;
        }
    }
}

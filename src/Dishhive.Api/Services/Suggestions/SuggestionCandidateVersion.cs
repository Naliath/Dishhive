using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dishhive.Api.Models;

namespace Dishhive.Api.Services.Suggestions;

internal static class SuggestionCandidateVersion
{
    public static string ForLocal(long revision, long confirmationVersion = 0) => $"local:{revision}:{confirmationVersion}";

    public static string ForLocal(RecipeOption recipe) =>
        $"{ForLocal(recipe.Revision, recipe.FactsConfirmationVersion)}:{Hash(new
        {
            recipe.FactsAssessed, Classes = recipe.ContainsClasses.Order(), recipe.TotalTimeMinutes
        })[..16]}";

    public static string ForLocal(Recipe recipe) => ForLocal(new RecipeOption
    {
        Id = recipe.Id, Title = recipe.Title, Revision = recipe.Revision,
        FactsConfirmationVersion = recipe.FactsConfirmationVersion, TotalTimeMinutes = recipe.TotalTimeMinutes,
        FactsAssessed = recipe.DietaryFactsStatus is DietaryFactsStatus.AiDetected or DietaryFactsStatus.UserConfirmed
            && (recipe.DietaryFactsAssessedRevision is null || recipe.DietaryFactsAssessedRevision == recipe.Revision),
        ContainsClasses = recipe.DietaryFactsStatus is DietaryFactsStatus.AiDetected or DietaryFactsStatus.UserConfirmed
            && (recipe.DietaryFactsAssessedRevision is null || recipe.DietaryFactsAssessedRevision == recipe.Revision)
            ? recipe.DietaryFacts.Select(fact => fact.IngredientClass).ToList() : []
    });

    public static long? LocalRevision(string? version) =>
        version?.StartsWith("local:", StringComparison.Ordinal) == true
        && long.TryParse(version.Split(':')[1], out var revision) ? revision : null;

    public static string ForContent(
        string? title,
        IEnumerable<string> ingredients,
        IEnumerable<IngredientClass>? facts = null,
        IEnumerable<string>? steps = null,
        int? totalTimeMinutes = null)
    {
        return "content:" + Hash(new
        {
            Title = title?.Trim().ToLowerInvariant(),
            Ingredients = ingredients.Select(value => value.Trim().ToLowerInvariant()),
            Facts = (facts ?? []).Order(),
            Steps = (steps ?? []).Select(value => value.Trim().ToLowerInvariant()),
            TotalTimeMinutes = totalTimeMinutes
        });
    }

    private static string Hash(object value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}

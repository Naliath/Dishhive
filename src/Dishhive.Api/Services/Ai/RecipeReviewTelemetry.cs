using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Dishhive.Api.Services.Ai;

public sealed record RecipeReviewSnapshot(long Revision, long Confirmation, DietaryFactsStatus Status,
    bool CurrentFacts, string? Language, IReadOnlyDictionary<string, string> Fields);

/// <summary>Stages non-text review evidence in the same transaction as the recipe write.</summary>
public sealed class RecipeReviewTelemetry(DishhiveDbContext context)
{
    public static RecipeReviewSnapshot Capture(Recipe recipe) => new(recipe.Revision, recipe.FactsConfirmationVersion,
        recipe.DietaryFactsStatus, recipe.DietaryFactsAssessedRevision == recipe.Revision, recipe.ContentLanguage,
        new Dictionary<string, string>
        {
            ["title"] = AiInputSnapshotScope.Hash(recipe.Title),
            ["description"] = AiInputSnapshotScope.Hash(recipe.Description),
            ["ingredients"] = AiInputSnapshotScope.Hash(recipe.Ingredients.OrderBy(item => item.SortOrder).Select(item => new { item.Name, item.Quantity, item.Unit })),
            ["steps"] = AiInputSnapshotScope.Hash(recipe.Steps.OrderBy(item => item.StepNumber).Select(item => item.Instruction)),
            ["timing"] = AiInputSnapshotScope.Hash(new { recipe.Servings, recipe.PrepTimeMinutes, recipe.CookTimeMinutes, recipe.TotalTimeMinutes }),
            ["facts"] = AiInputSnapshotScope.Hash(recipe.DietaryFacts.Select(fact => fact.IngredientClass).Order())
        });

    public async Task StageAsync(Recipe recipe, RecipeReviewSnapshot before, bool factsConfirmed, CancellationToken token)
    {
        var after = Capture(recipe);
        var changed = before.Fields.Keys.Where(key => before.Fields[key] != after.Fields[key]).ToList();
        var inputChanges = before.Revision == after.Revision ? Array.Empty<string>() : ["recipeRevision"];
        var textChanged = changed.Any(field => field is "title" or "description" or "ingredients" or "steps");
        if (changed.Any(field => field != "facts"))
        {
            var translatedOperation = !textChanged || before.Language == null ? null : await context.RecipeEnrichmentJobs
                .Where(job => job.RecipeId == recipe.Id && job.RecipeRevision == before.Revision
                    && job.Task == RecipeEnrichmentTask.Translation && job.TargetLanguage == before.Language
                    && job.State == RecipeEnrichmentJobState.Completed)
                .OrderByDescending(job => job.UpdatedAt).Select(job => job.OperationId).FirstOrDefaultAsync(token);
            Add(translatedOperation == null ? "recipeEdited" : "textEditedAfterTranslation",
                translatedOperation == null ? "recipe.edit" : "recipe.translate", translatedOperation, changed.Where(field => field != "facts"));
        }
        if (factsConfirmed)
        {
            var operation = await context.RecipeEnrichmentJobs.Where(job => job.RecipeId == recipe.Id
                    && job.RecipeRevision == before.Revision && job.FactsConfirmationVersion == before.Confirmation
                    && job.Task == RecipeEnrichmentTask.Facts && job.State == RecipeEnrichmentJobState.Completed)
                .OrderByDescending(job => job.UpdatedAt).Select(job => job.OperationId).FirstOrDefaultAsync(token);
            var kind = before.Revision != after.Revision ? "factsSetAfterSourceEdit"
                : before.Status == DietaryFactsStatus.AiDetected && before.CurrentFacts && changed.Contains("facts")
                    ? "factsCorrected" : "factsConfirmed";
            Add(kind, "recipe.facts", operation, changed.Where(field => field == "facts"));
        }

        void Add(string kind, string task, string? operation, IEnumerable<string> fields) => context.AiReviewEvents.Add(new()
        {
            RecipeId = recipe.Id, Task = task, Kind = kind, OperationId = string.IsNullOrEmpty(operation) ? null : operation,
            SourceRevision = before.Revision, ObservedRevision = after.Revision,
            SourceConfirmationVersion = before.Confirmation, ObservedConfirmationVersion = after.Confirmation,
            ChangedFieldsJson = JsonSerializer.Serialize(fields), InputChangesJson = JsonSerializer.Serialize(inputChanges)
        });
    }
}

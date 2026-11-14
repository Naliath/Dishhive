using Dishhive.Api.Data;
using Dishhive.Api.Models;

namespace Dishhive.Api.Services.Facts;

/// <summary>
/// Owns facts-state transitions that must commit atomically with recipe changes.
/// Model execution remains in the durable enrichment worker.
/// </summary>
public static class RecipeFactsStateService
{
    public static void Invalidate(Recipe recipe, long? assessedRevisionFallback = null)
    {
        Archive(recipe, assessedRevisionFallback);
        recipe.DietaryFactsStatus = DietaryFactsStatus.Stale;
    }

    public static void Replace(
        DishhiveDbContext context,
        Recipe recipe,
        IEnumerable<IngredientClass> classes,
        DietaryFactsStatus status)
    {
        Archive(recipe);
        context.RecipeDietaryFacts.RemoveRange(recipe.DietaryFacts);
        recipe.DietaryFacts.Clear();
        foreach (var ingredientClass in classes.Distinct())
            recipe.DietaryFacts.Add(new RecipeDietaryFact { Recipe = recipe, IngredientClass = ingredientClass });
        recipe.DietaryFactsStatus = status;
        recipe.DietaryFactsAssessedAt = DateTime.UtcNow;
        recipe.DietaryFactsAssessedRevision = recipe.Revision;
        if (status == DietaryFactsStatus.UserConfirmed) recipe.FactsConfirmationVersion++;
    }

    public static void Archive(Recipe recipe, long? assessedRevisionFallback = null)
    {
        if (recipe.DietaryFactsStatus == DietaryFactsStatus.Unassessed
            || recipe.DietaryFactsAssessedAt is null) return;
        var contains = string.Join(',', recipe.DietaryFacts
            .Select(fact => EnumNames.ToName(fact.IngredientClass)).Order());
        var revision = recipe.DietaryFactsAssessedRevision
            ?? assessedRevisionFallback
            ?? recipe.Revision;
        if (recipe.DietaryFactsHistory.Any(history =>
                history.RecipeRevision == revision
                && history.AssessedAt == recipe.DietaryFactsAssessedAt
                && (history.Status == recipe.DietaryFactsStatus
                    || recipe.DietaryFactsStatus == DietaryFactsStatus.Pending)
                && history.Contains == contains)) return;
        recipe.DietaryFactsHistory.Add(new RecipeDietaryFactHistory
        {
            Recipe = recipe,
            RecipeRevision = revision,
            Status = recipe.DietaryFactsStatus,
            Contains = contains,
            AssessedAt = recipe.DietaryFactsAssessedAt
        });
    }
}

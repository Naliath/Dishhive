namespace Dishhive.Api.Services.Suggestions;

/// <summary>Content-bound semantic approvals; an unverified name cannot satisfy a food requirement.</summary>
internal static class PlanningDishIdentity
{
    internal static string Key(MealSuggestion suggestion) => suggestion.RecipeId is { } recipeId
        ? "local-dish:" + Dishhive.Api.Services.Ai.AiInputSnapshotScope.Hash(new
            { RecipeId = recipeId, suggestion.CandidateVersion, suggestion.DishName,
                Ingredients = suggestion.ExternalIngredients.Order(StringComparer.Ordinal) })
        : !string.IsNullOrWhiteSpace(suggestion.CandidateVersion) ? suggestion.CandidateVersion
        : SuggestionCandidateVersion.ForContent(suggestion.DishName ?? "", suggestion.ExternalIngredients);

    internal static PlanningIntent WithExternalMatches(PlanningIntent intent, IReadOnlyList<ExternalRecipeCandidate> candidates) => intent with
    {
        Constraints = intent.Constraints.Select(constraint => constraint.SourceHost is null ? constraint : constraint with
        {
            VerifiedDishVersions = candidates.Where(candidate => candidate.ResearchConstraintIds.Contains(constraint.Id))
                .Select(candidate => SuggestionCandidateVersion.ForContent(candidate.Title, candidate.Ingredients,
                    steps: candidate.Instructions, totalTimeMinutes: candidate.TotalTimeMinutes)).Distinct().ToList()
        }).ToList()
    };

    internal static async Task<PlanningIntent> ApproveAsync(PlanningIntent intent, MealSuggestionRequest request,
        IReadOnlyList<MealSuggestion> candidates, IRecipeDishMatcher? matcher, string operationId,
        int reservedAttempts, CancellationToken cancellationToken)
    {
        var constraints = new List<PlanningConstraint>();
        foreach (var constraint in intent.Constraints)
        {
            if (!constraint.RequiresDishMatch || constraint.SourceHost is not null || constraint.DishRequest is null)
            { constraints.Add(constraint); continue; }
            var approved = constraint.VerifiedDishVersions.ToHashSet(StringComparer.Ordinal);
            var options = candidates.Where(candidate => candidate.SourceUrl is null)
                .Where(candidate => constraint.RequiredRecipeIds.Count == 0 || candidate.RecipeId is { } id && constraint.RequiredRecipeIds.Contains(id))
                .Where(candidate => candidate.RecipeId is not { } id || !request.AllergyExcludedRecipeIds.Contains(id))
                .DistinctBy(Key).Take(28).ToList();
            foreach (var candidate in options.Where(candidate => candidate.RecipeId is { } id && constraint.RequiredRecipeIds.Contains(id)))
                approved.Add(Key(candidate)); // An explicitly resolved saved recipe has stable identity already.
            if (matcher is not null)
            foreach (var batch in options.Where(candidate => !approved.Contains(Key(candidate))).Chunk(4))
            {
                if ((Dishhive.Api.Services.Ai.AiOperationBudget.Current?.RemainingAttempts ?? int.MaxValue) <= reservedAttempts) break;
                var matching = await matcher.MatchAsync(constraint.DishRequest, batch.Select(candidate => new DishMatchCandidate(
                    Key(candidate), candidate.DishName ?? "", candidate.RecipeId is { } id
                        ? request.RecipeAllergens.GetValueOrDefault(id)?.Ingredients ?? [] : candidate.ExternalIngredients,
                    [])).ToList(), operationId, cancellationToken);
                foreach (var key in matching ?? []) approved.Add(key);
            }
            constraints.Add(constraint with { VerifiedDishVersions = approved.ToList() });
        }
        return intent with { Constraints = constraints };
    }
}

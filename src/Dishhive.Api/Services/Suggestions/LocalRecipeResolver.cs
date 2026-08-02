namespace Dishhive.Api.Services.Suggestions;

/// <summary>
/// Resolves model output to a local recipe without fuzzy matching. Stable ids are
/// authoritative; current and original titles remain a compatibility fallback only
/// when they identify exactly one recipe.
/// </summary>
internal static class LocalRecipeResolver
{
    public static RecipeOption? ById(
        IReadOnlyList<RecipeOption> recipes,
        string? localRecipeId)
    {
        if (!Guid.TryParse(localRecipeId?.Trim(), out var id))
        {
            return null;
        }

        return recipes.FirstOrDefault(recipe => recipe.Id == id);
    }

    public static RecipeOption? ByTitle(
        IReadOnlyList<RecipeOption> recipes,
        params string?[] titles)
    {
        foreach (var title in titles)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var value = title.Trim();
            var matches = recipes
                .Where(recipe => string.Equals(recipe.Title, value, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(recipe.OriginalTitle)
                        && string.Equals(recipe.OriginalTitle.Trim(), value, StringComparison.OrdinalIgnoreCase)))
                .DistinctBy(recipe => recipe.Id)
                .Take(2)
                .ToList();
            if (matches.Count == 1)
            {
                return matches[0];
            }
        }

        return null;
    }

    public static RecipeOption? Resolve(
        IReadOnlyList<RecipeOption> recipes,
        string? localRecipeId,
        string? recipeTitle,
        string? dishName) =>
        ById(recipes, localRecipeId) ?? ByTitle(recipes, recipeTitle, dishName);
}

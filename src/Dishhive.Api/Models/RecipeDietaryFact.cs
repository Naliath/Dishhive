namespace Dishhive.Api.Models;

/// <summary>
/// Assessment state of a recipe's dietary facts. Tri-state on purpose: an absent
/// fact on an <see cref="Unassessed"/> recipe means "unknown", never "safe" — only
/// assessed recipes participate in exact-match filtering and warnings.
/// </summary>
public enum DietaryFactsStatus
{
    Unassessed = 0,
    AiDetected = 1,
    UserConfirmed = 2
}

/// <summary>
/// One canonical ingredient class a recipe contains (see <see cref="IngredientClass"/>).
/// Written by the AI facts assessment at add/import time or by the user; the set is
/// replaced wholesale on each (re)assessment or user edit.
/// </summary>
public class RecipeDietaryFact
{
    public Guid RecipeId { get; set; }

    public Recipe? Recipe { get; set; }

    public IngredientClass IngredientClass { get; set; }
}

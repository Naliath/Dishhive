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
    UserConfirmed = 2,
    Stale = 3,
    Pending = 4,
    Failed = 5
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

/// <summary>
/// Immutable audit snapshot retained whenever the current dietary assessment is
/// replaced or invalidated. Contains is stored as stable enum names so historical
/// evidence is not confused with the recipe's current fact rows.
/// </summary>
public class RecipeDietaryFactHistory
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public Recipe? Recipe { get; set; }
    public long RecipeRevision { get; set; }
    public DietaryFactsStatus Status { get; set; }
    public string Contains { get; set; } = string.Empty;
    public DateTime? AssessedAt { get; set; }
    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;
}

using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.DTOs;

/// <summary>
/// A recipe's dietary facts: the canonical ingredient classes it contains plus the
/// tri-state assessment status. An Unassessed recipe's empty Contains list means
/// "unknown", never "contains nothing" — consumers must check the status.
/// </summary>
public class RecipeDietaryFactsDto
{
    /// <summary>IngredientClass names (e.g. "Milk", "Gluten", "Pork")</summary>
    public List<string> Contains { get; set; } = [];

    public DietaryFactsStatus Status { get; set; }

    public DateTime? AssessedAt { get; set; }
}

/// <summary>Slim DTO for recipe lists and planner autocomplete</summary>
public class RecipeListItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;

    public int Servings { get; set; }
    public int? TotalTimeMinutes { get; set; }
    public string? Category { get; set; }

    /// <summary>
    /// Local Dishhive image endpoint, or null when no local image is stored
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>True when the image bytes are stored locally in Dishhive</summary>
    public bool HasLocalImage { get; set; }

    public string? SourceProvider { get; set; }

    /// <summary>Organization tag names (user-curated, see recipe-organization.md)</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Ids of the manual collections this recipe belongs to</summary>
    public List<Guid> CookbookIds { get; set; } = new();
}

/// <summary>Full recipe DTO for the detail view</summary>
public class RecipeDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? OriginalTitle { get; set; }
    public string? Description { get; set; }

    public string? OriginalDescription { get; set; }
    public string? ContentLanguage { get; set; }
    public int Servings { get; set; }
    public int? PrepTimeMinutes { get; set; }
    public int? CookTimeMinutes { get; set; }
    public int? TotalTimeMinutes { get; set; }
    public string? Category { get; set; }
    public string? Keywords { get; set; }

    /// <summary>
    /// Local Dishhive image endpoint, or null when no local image is stored
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>True when the image bytes are stored locally in Dishhive</summary>
    public bool HasLocalImage { get; set; }

    /// <summary>
    /// Original remote image URL retained for traceability. Unlike ImageUrl, this is
    /// never used for display when a local image exists.
    /// </summary>
    public string? ImageSourceUrl { get; set; }

    public string? VideoUrl { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourceProvider { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<RecipeIngredientDto> Ingredients { get; set; } = new();
    public List<RecipeStepDto> Steps { get; set; } = new();

    /// <summary>Organization tag names (user-curated, see recipe-organization.md)</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Ids of the manual collections this recipe belongs to</summary>
    public List<Guid> CookbookIds { get; set; } = new();

    /// <summary>Dietary facts (contained ingredient classes + assessment status)</summary>
    public RecipeDietaryFactsDto DietaryFacts { get; set; } = new();
}

public class RecipeIngredientDto
{
    public Guid Id { get; set; }
    public int SortOrder { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public string OriginalText { get; set; } = string.Empty;
    public decimal? OriginalQuantity { get; set; }
    public string? OriginalUnit { get; set; }
}

public class RecipeStepDto
{
    public Guid Id { get; set; }
    public int StepNumber { get; set; }
    public string Instruction { get; set; } = string.Empty;
    public string? OriginalInstruction { get; set; }
}

public class CreateRecipeDto
{
    [Required]
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? OriginalTitle { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(2000)]
    public string? OriginalDescription { get; set; }

    [MaxLength(10)]
    public string? ContentLanguage { get; set; }

    [Range(1, 100)]
    public int Servings { get; set; } = 4;

    public int? PrepTimeMinutes { get; set; }
    public int? CookTimeMinutes { get; set; }
    public int? TotalTimeMinutes { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    [MaxLength(500)]
    public string? Keywords { get; set; }

    [MaxLength(1000)]
    public string? ImageUrl { get; set; }

    [MaxLength(1000)]
    public string? VideoUrl { get; set; }

    public List<CreateRecipeIngredientDto> Ingredients { get; set; } = new();
    public List<CreateRecipeStepDto> Steps { get; set; } = new();

    /// <summary>Organization tag names; tags are created when new, synced on update</summary>
    [MaxLength(20)]
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Contained IngredientClass names. Null = untouched: a create queues an AI
    /// assessment, an update keeps the stored facts (unless the ingredients changed,
    /// which re-queues). Non-null = the user set them explicitly → UserConfirmed.
    /// </summary>
    public List<string>? ContainsClasses { get; set; }
}

public class CreateRecipeIngredientDto
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public decimal? Quantity { get; set; }

    [MaxLength(50)]
    public string? Unit { get; set; }

    /// <summary>Verbatim line; defaults to the composed name/quantity when omitted</summary>
    [MaxLength(300)]
    public string? OriginalText { get; set; }
}

public class CreateRecipeStepDto
{
    [Required]
    [MaxLength(2000)]
    public string Instruction { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? OriginalInstruction { get; set; }
}

/// <summary>Update replaces ingredients and steps wholesale (see docs/features/recipe-store.md)</summary>
public class UpdateRecipeDto : CreateRecipeDto
{
}

public class ImportRecipeRequestDto
{
    [Required]
    [MaxLength(1000)]
    public string Url { get; set; } = string.Empty;
}

/// <summary>Outcome of importing a recipe file (schema.org Recipe JSON)</summary>
public class RecipeFileImportResultDto
{
    public int Created { get; set; }

    public int Updated { get; set; }

    public int Skipped => SkippedRecipes.Count;

    public int Total => Created + Updated + Skipped;

    public List<RecipeFileImportSkippedDto> SkippedRecipes { get; set; } = new();
}

public class RecipeFileImportSkippedDto
{
    public string Title { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

public class RecipeTagDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// A collection: a named set of explicitly curated recipes. Manual collections are
/// user-managed; auto collections are computed (read-only) and use slug ids like
/// "auto-quick" instead of a Guid.
/// </summary>
public class CookbookDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>"manual" or "auto"</summary>
    public string Kind { get; set; } = "manual";

    public int RecipeCount { get; set; }
}

public class CreateCookbookDto
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
}

public class UpdateCookbookDto : CreateCookbookDto
{
}

/// <summary>A computed auto collection with its enabled state, for the settings UI</summary>
public class AutoCollectionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int RecipeCount { get; set; }
    public bool Enabled { get; set; }
}

public class ToggleAutoCollectionDto
{
    public bool Enabled { get; set; }
}

/// <summary>Bulk add of recipes to a collection</summary>
public class CookbookRecipesRequestDto
{
    [Required]
    public List<Guid> RecipeIds { get; set; } = new();
}

/// <summary>Full sync of one recipe's collection memberships (manual collections only)</summary>
public class RecipeCookbooksRequestDto
{
    [Required]
    public List<Guid> CookbookIds { get; set; } = new();
}

/// <summary>A known recipe source for the week-planner's @[Source] picker</summary>
public record RecipeSourceDto(string Name, string Host);

/// <summary>User-set dietary facts for a recipe (PUT …/facts → UserConfirmed)</summary>
public class UpdateRecipeFactsDto
{
    /// <summary>IngredientClass names the recipe contains (may be empty = contains none)</summary>
    [Required]
    public List<string> Contains { get; set; } = [];
}

/// <summary>
/// Library-wide facts progress for the settings page: how much of the library is
/// assessed and how the background queue is doing (polled during a backfill)
/// </summary>
public class RecipeFactsStatusDto
{
    public int Unassessed { get; set; }
    public int AiDetected { get; set; }
    public int UserConfirmed { get; set; }

    /// <summary>Recipes waiting in (or being processed by) the assessment queue</summary>
    public int QueueDepth { get; set; }

    public bool Running { get; set; }

    /// <summary>Whether AI is configured, i.e. whether assessment can run at all</summary>
    public bool Available { get; set; }

    public string? LastError { get; set; }
}

/// <summary>Result of a backfill request: how many recipes were newly queued</summary>
public class RecipeFactsBackfillResultDto
{
    public int Enqueued { get; set; }
}

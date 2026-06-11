using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.DTOs;

/// <summary>Slim DTO for recipe lists and planner autocomplete</summary>
public class RecipeListItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Servings { get; set; }
    public int? TotalTimeMinutes { get; set; }
    public string? Category { get; set; }

    /// <summary>
    /// Image location: the local image endpoint when the image is stored in Dishhive,
    /// otherwise the remote source URL (or null)
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>True when the image bytes are stored locally in Dishhive</summary>
    public bool HasLocalImage { get; set; }

    public string? SourceProvider { get; set; }

    /// <summary>Organization tag names (user-curated, see recipe-organization.md)</summary>
    public List<string> Tags { get; set; } = new();
}

/// <summary>Full recipe DTO for the detail view</summary>
public class RecipeDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Servings { get; set; }
    public int? PrepTimeMinutes { get; set; }
    public int? CookTimeMinutes { get; set; }
    public int? TotalTimeMinutes { get; set; }
    public string? Category { get; set; }
    public string? Keywords { get; set; }

    /// <summary>
    /// Image location: the local image endpoint when the image is stored in Dishhive,
    /// otherwise the remote source URL (or null)
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>True when the image bytes are stored locally in Dishhive</summary>
    public bool HasLocalImage { get; set; }

    public string? VideoUrl { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourceProvider { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<RecipeIngredientDto> Ingredients { get; set; } = new();
    public List<RecipeStepDto> Steps { get; set; } = new();

    /// <summary>Organization tag names (user-curated, see recipe-organization.md)</summary>
    public List<string> Tags { get; set; } = new();
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
}

public class CreateRecipeDto
{
    [Required]
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

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

public class RecipeTagDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>A cookbook: a named, saved recipe filter</summary>
public class CookbookDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SearchTerm { get; set; }
    public string? Category { get; set; }
    public List<string> Tags { get; set; } = new();
}

public class CreateCookbookDto
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? SearchTerm { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    [MaxLength(20)]
    public List<string> Tags { get; set; } = new();
}

public class UpdateCookbookDto : CreateCookbookDto
{
}

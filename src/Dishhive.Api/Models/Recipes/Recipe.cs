using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.Recipes;

public class Recipe
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public int Servings { get; set; } = 4;

    [MaxLength(1000)]
    public string? ImageUrl { get; set; }

    [MaxLength(1000)]
    public string? VideoUrl { get; set; }

    [MaxLength(1000)]
    public string? SourceUrl { get; set; }

    /// <summary>Provider key set by the recipe-import pipeline (e.g. "dagelijksekost"). Null for manual recipes.</summary>
    [MaxLength(100)]
    public string? SourceProviderKey { get; set; }

    /// <summary>Verbatim source payload (typically JSON) preserved for traceability and manual correction.</summary>
    public string? SourceRawPayload { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<RecipeIngredient> Ingredients { get; set; } = new();
    public List<RecipeStep> Steps { get; set; } = new();
    public List<RecipeTag> Tags { get; set; } = new();
}

public class RecipeIngredient
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public Recipe? Recipe { get; set; }

    public int Order { get; set; }

    [Required]
    [MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    public decimal? Quantity { get; set; }

    [MaxLength(50)]
    public string? Unit { get; set; }

    /// <summary>Quantity as it appeared on the source page (preserved verbatim for manual correction).</summary>
    public decimal? OriginalQuantity { get; set; }

    [MaxLength(50)]
    public string? OriginalUnit { get; set; }

    [MaxLength(100)]
    public string? Section { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}

public class RecipeStep
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public Recipe? Recipe { get; set; }

    public int Order { get; set; }

    [Required]
    public string Text { get; set; } = string.Empty;
}

public class RecipeTag
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public Recipe? Recipe { get; set; }

    [Required]
    [MaxLength(100)]
    public string Tag { get; set; } = string.Empty;
}

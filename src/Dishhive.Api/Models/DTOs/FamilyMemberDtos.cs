using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.DTOs;

/// <summary>
/// One dietary tag on one member: the shared tag name plus this member's reading of
/// it as excluded <see cref="IngredientClass"/> names ("my vegetarian eats fish,
/// yours doesn't" — the definition lives on the member↔tag link, not the tag).
/// </summary>
public class DietaryTagEntryDto
{
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Excluded ingredient class names for this member. Null in a request means
    /// "no explicit definition": a new link is seeded from the built-in presets, an
    /// existing link keeps its stored set. Responses always carry the resolved set
    /// (possibly empty = tag is not machine-checkable, prompt-only).
    /// </summary>
    public List<string>? ExcludedClasses { get; set; }
}

public class FamilyMemberDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsGuest { get; set; }

    /// <summary>Allergy/intolerance tags (e.g. "Shellfish", "Lactose") with their per-member excluded classes</summary>
    public List<DietaryTagEntryDto> AllergyTags { get; set; } = [];

    /// <summary>Diet tags (e.g. "Vegetarian", "No pork") with their per-member excluded classes</summary>
    public List<DietaryTagEntryDto> DietTags { get; set; } = [];

    public string? PreferenceNotes { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateFamilyMemberDto
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool IsGuest { get; set; } = false;

    /// <summary>Allergy/intolerance tags; tags are created when new</summary>
    [MaxLength(20)]
    public List<DietaryTagEntryDto> AllergyTags { get; set; } = [];

    /// <summary>Diet tags; tags are created when new</summary>
    [MaxLength(20)]
    public List<DietaryTagEntryDto> DietTags { get; set; } = [];

    [MaxLength(1000)]
    public string? PreferenceNotes { get; set; }
}

public class FamilyMemberFavoriteDto
{
    public Guid Id { get; set; }
    public Guid FamilyMemberId { get; set; }
    public Guid? RecipeId { get; set; }
    public string? DishName { get; set; }
}

public class CreateFamilyMemberFavoriteDto
{
    /// <summary>Favorite known recipe; at least one of RecipeId/DishName is required</summary>
    public Guid? RecipeId { get; set; }

    [MaxLength(200)]
    public string? DishName { get; set; }
}

public class UpdateFamilyMemberDto
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool IsGuest { get; set; }

    /// <summary>Allergy/intolerance tags; the member's tags are synced to this list</summary>
    [MaxLength(20)]
    public List<DietaryTagEntryDto> AllergyTags { get; set; } = [];

    /// <summary>Diet tags; the member's tags are synced to this list</summary>
    [MaxLength(20)]
    public List<DietaryTagEntryDto> DietTags { get; set; } = [];

    [MaxLength(1000)]
    public string? PreferenceNotes { get; set; }

    public bool IsActive { get; set; } = true;
}

public class DietaryTagDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DietaryTagKind Kind { get; set; }
}

/// <summary>Preset excluded classes for a tag name (family-form preview)</summary>
public class DietaryTagPresetDto
{
    public List<string> ExcludedClasses { get; set; } = [];
}

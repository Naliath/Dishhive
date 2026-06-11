using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.DTOs;

public class FamilyMemberDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsGuest { get; set; }

    /// <summary>Allergy/intolerance tag names (e.g. "Shellfish", "Lactose")</summary>
    public List<string> AllergyTags { get; set; } = [];

    /// <summary>Diet tag names (e.g. "Vegetarian", "No pork")</summary>
    public List<string> DietTags { get; set; } = [];

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

    /// <summary>Allergy/intolerance tag names; tags are created when new</summary>
    [MaxLength(20)]
    public List<string> AllergyTags { get; set; } = [];

    /// <summary>Diet tag names; tags are created when new</summary>
    [MaxLength(20)]
    public List<string> DietTags { get; set; } = [];

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

    /// <summary>Allergy/intolerance tag names; the member's tags are synced to this list</summary>
    [MaxLength(20)]
    public List<string> AllergyTags { get; set; } = [];

    /// <summary>Diet tag names; the member's tags are synced to this list</summary>
    [MaxLength(20)]
    public List<string> DietTags { get; set; } = [];

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

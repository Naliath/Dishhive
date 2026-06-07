using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.Family;

public class FamilyMember
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<FamilyMemberPreference> Preferences { get; set; } = new();
}

public class Guest
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum PreferenceKind
{
    Allergy = 0,
    Intolerance = 1,
    Dietary = 2,
    Dislike = 3,
    Favorite = 4
}

public class FamilyMemberPreference
{
    public Guid Id { get; set; }

    public Guid FamilyMemberId { get; set; }
    public FamilyMember? FamilyMember { get; set; }

    public PreferenceKind Kind { get; set; }

    [Required]
    [MaxLength(200)]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Optional link to a known recipe (only meaningful when <see cref="Kind"/> == <see cref="PreferenceKind.Favorite"/>).
    /// </summary>
    public Guid? RecipeId { get; set; }
}

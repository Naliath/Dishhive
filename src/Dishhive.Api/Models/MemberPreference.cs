using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// A personal food preference, allergy, intolerance, or constraint for a family member.
/// </summary>
public class MemberPreference
{
    public Guid Id { get; set; }

    public Guid FamilyMemberId { get; set; }
    public FamilyMember FamilyMember { get; set; } = null!;

    public PreferenceType PreferenceType { get; set; }

    /// <summary>The value of the preference (e.g., "peanuts", "gluten", "vegetarian").</summary>
    [Required]
    [MaxLength(200)]
    public string Value { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum PreferenceType
{
    Allergy,
    Intolerance,
    DietaryConstraint,
    Dislike,
    Preference
}

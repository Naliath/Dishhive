using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// Represents a household member or temporary guest who attends planned meals
/// </summary>
public class FamilyMember
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Guests are temporary attendees; they are not preselected for meals
    /// </summary>
    public bool IsGuest { get; set; } = false;

    /// <summary>
    /// Structured allergy and diet tags (see <see cref="DietaryTag"/>);
    /// replaced the former free-text Allergies/DietaryConstraints fields
    /// </summary>
    public List<FamilyMemberDietaryTag> DietaryTags { get; set; } = [];

    /// <summary>
    /// Likes and dislikes, free text
    /// </summary>
    [MaxLength(1000)]
    public string? PreferenceNotes { get; set; }

    /// <summary>
    /// Soft-delete flag; inactive members are hidden but keep their meal history
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

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
    /// Allergies and intolerances, free text (e.g. "nuts, lactose")
    /// </summary>
    [MaxLength(500)]
    public string? Allergies { get; set; }

    /// <summary>
    /// Dietary constraints, free text (e.g. "vegetarian, no pork")
    /// </summary>
    [MaxLength(500)]
    public string? DietaryConstraints { get; set; }

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

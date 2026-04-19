using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// A household family member or temporary guest.
/// </summary>
public class FamilyMember
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>When true, this is a temporary guest rather than a permanent household member.</summary>
    public bool IsGuest { get; set; } = false;

    public DateOnly? GuestFrom { get; set; }
    public DateOnly? GuestUntil { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MemberPreference> Preferences { get; set; } = [];
    public ICollection<FavoriteDish> FavoriteDishes { get; set; } = [];
}

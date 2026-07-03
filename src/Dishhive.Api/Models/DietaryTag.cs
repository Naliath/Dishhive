using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// Whether a tag is an allergy/intolerance (hard "must not contain") or a
/// diet/lifestyle constraint (e.g. vegetarian, no pork)
/// </summary>
public enum DietaryTagKind
{
    Allergy = 0,
    Diet = 1
}

/// <summary>
/// A reusable allergy or diet tag shared across family members (replaces the
/// former free-text Allergies/DietaryConstraints fields). Tags are created
/// implicitly when assigned to a member and removed when no member uses them,
/// so the tag pool always reflects real usage.
/// </summary>
public class DietaryTag
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    public DietaryTagKind Kind { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<FamilyMemberDietaryTag> Members { get; set; } = [];
}

/// <summary>Join table linking family members to their dietary tags</summary>
public class FamilyMemberDietaryTag
{
    public Guid FamilyMemberId { get; set; }

    public FamilyMember? FamilyMember { get; set; }

    public Guid DietaryTagId { get; set; }

    public DietaryTag? DietaryTag { get; set; }

    /// <summary>
    /// The ingredient classes this tag excludes FOR THIS MEMBER — the tag itself is a
    /// shared label ("Vegetarian"), but its machine-checkable meaning is per member
    /// (one vegetarian eats fish, another doesn't). Seeded from
    /// <see cref="DietaryTagPresets"/> when the link is created without explicit
    /// classes; empty for tags with no expressible definition (e.g. certification
    /// diets like kosher), which then stay prompt-only like before.
    /// </summary>
    public List<IngredientClass> ExcludedClasses { get; set; } = [];
}


namespace Dishhive.Domain.Entities.Family;

using Dishhive.Domain.Common;

/// <summary>
/// Represents a dietary preference or restriction for a family member.
/// Examples: vegetarian, vegan, gluten-free, halal, etc.
/// </summary>
public class DietaryPreference : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid FamilyMemberId { get; set; }
}


namespace Dishhive.Domain.Entities.Family;

using Dishhive.Domain.Common;

/// <summary>
/// Represents a household or guest member.
/// </summary>
public class FamilyMember : BaseEntity
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public MemberType Type { get; set; } = MemberType.Household;
    public DateTime? ArrivedDate { get; set; }
    public DateTime? DepartureDate { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Allergy> Allergies { get; set; } = new List<Allergy>();
    public ICollection<DietaryPreference> DietaryPreferences { get; set; } = new List<DietaryPreference>();
    public ICollection<DishFavorite> Favorites { get; set; } = new List<DishFavorite>();
}

public enum MemberType
{
    Household = 0,
    Guest = 1
}

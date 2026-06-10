
namespace Dishhive.Domain.Entities.Planner;

using Dishhive.Domain.Common;

/// <summary>
/// Represents a planned meal slot on a specific day.
/// </summary>
public class MealSlot : BaseEntity
{
    public DateTime Date { get; set; }
    public MealType MealType { get; set; }
    public string? Title { get; set; }
    public string? VagueInstruction { get; set; }
    public Guid? RecipeId { get; set; }
    public bool IsLeftovers { get; set; }
    public Guid? FreezyItemId { get; set; }

    // Family members present for this meal
    public ICollection<Guid> FamilyMemberIds { get; set; } = new List<Guid>();
}

/// <summary>
/// Type of meal slot in a day.
/// </summary>
public enum MealType
{
    Breakfast = 0,
    Lunch = 1,
    Dinner = 2,
    Snack = 3
}

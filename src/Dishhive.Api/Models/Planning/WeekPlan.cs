using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.Planning;

public enum MealType
{
    Breakfast = 0,
    Lunch = 1,
    Dinner = 2,
    Snack = 3
}

public enum IntentTag
{
    None = 0,
    Fish = 1,
    Quick = 2,
    Leftovers = 3,
    Vegetarian = 4,
    Other = 99
}

public class WeekPlan
{
    public Guid Id { get; set; }

    /// <summary>Date of the Monday that starts the week.</summary>
    public DateOnly WeekStart { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<MealSlot> Slots { get; set; } = new();
}

public class MealSlot
{
    public Guid Id { get; set; }
    public Guid WeekPlanId { get; set; }
    public WeekPlan? WeekPlan { get; set; }

    public DayOfWeek DayOfWeek { get; set; }
    public MealType MealType { get; set; }

    public Guid? RecipeId { get; set; }

    [MaxLength(500)]
    public string? VagueIntent { get; set; }

    public IntentTag IntentTag { get; set; } = IntentTag.None;

    /// <summary>Opaque Freezy item identifier when this slot is sourced from Freezy.</summary>
    [MaxLength(100)]
    public string? FrozenItemRef { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public List<MealSlotAttendee> Attendees { get; set; } = new();
}

public class MealSlotAttendee
{
    public Guid Id { get; set; }

    public Guid MealSlotId { get; set; }
    public MealSlot? MealSlot { get; set; }

    /// <summary>Exactly one of <see cref="FamilyMemberId"/> or <see cref="GuestId"/> must be set.</summary>
    public Guid? FamilyMemberId { get; set; }
    public Guid? GuestId { get; set; }
}

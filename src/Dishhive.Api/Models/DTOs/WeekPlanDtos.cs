using System.ComponentModel.DataAnnotations;
using Dishhive.Api.Models.Planning;

namespace Dishhive.Api.Models.DTOs;

public sealed record WeekPlanDto(
    Guid Id,
    DateOnly WeekStart,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<MealSlotDto> Slots);

public sealed record MealSlotDto(
    Guid Id,
    Guid WeekPlanId,
    DayOfWeek DayOfWeek,
    MealType MealType,
    Guid? RecipeId,
    string? RecipeTitle,
    string? VagueIntent,
    IntentTag IntentTag,
    string? FrozenItemRef,
    string? Notes,
    IReadOnlyList<MealSlotAttendeeDto> Attendees);

public sealed record MealSlotAttendeeDto(Guid Id, Guid? FamilyMemberId, Guid? GuestId);

public sealed class CreateWeekPlanDto
{
    public DateOnly WeekStart { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
}

public sealed class UpdateWeekPlanDto
{
    [MaxLength(1000)] public string? Notes { get; set; }
}

/// <summary>
/// Used by <c>PUT /api/week-plans/{id}/slots/{slotId}</c>. Setting all of
/// <see cref="RecipeId"/>, <see cref="VagueIntent"/>, <see cref="FrozenItemRef"/>
/// to null clears the slot.
/// </summary>
public sealed class UpdateMealSlotDto
{
    public Guid? RecipeId { get; set; }
    [MaxLength(500)] public string? VagueIntent { get; set; }
    public IntentTag IntentTag { get; set; } = IntentTag.None;
    [MaxLength(100)] public string? FrozenItemRef { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public sealed class UpdateAttendeesDto
{
    public List<Guid> FamilyMemberIds { get; set; } = new();
    public List<Guid> GuestIds { get; set; } = new();
}

namespace Dishhive.Api.Models;

/// <summary>
/// Join entity: which family members/guests are present for a planned meal
/// </summary>
public class PlannedMealAttendee
{
    public Guid PlannedMealId { get; set; }

    public PlannedMeal? PlannedMeal { get; set; }

    public Guid FamilyMemberId { get; set; }

    public FamilyMember? FamilyMember { get; set; }
}

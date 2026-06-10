
namespace Dishhive.Domain.Entities.Family;

using Dishhive.Domain.Common;

/// <summary>
/// Represents an allergy or intolerance for a family member.
/// </summary>
public class Allergy : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid FamilyMemberId { get; set; }
}

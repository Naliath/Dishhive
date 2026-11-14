using Dishhive.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Suggestions;

public sealed record AttendanceResolution(
    IReadOnlyList<Guid> AttendeeIds,
    IReadOnlyList<Guid> InvalidIds,
    bool UsedHouseholdDefault);

public sealed class InvalidAttendanceSelectionException(IReadOnlyList<Guid> invalidIds)
    : ArgumentException("Attendance contains inactive or unknown members")
{
    public IReadOnlyList<Guid> InvalidIds { get; } = invalidIds;
}

/// <summary>
/// An empty selection means active household members; an explicit selection may
/// include active guests. Inactive or unknown explicit members are rejected.
/// </summary>
public sealed class AttendancePolicy(DishhiveDbContext context)
{
    public async Task<AttendanceResolution> ResolveAsync(
        IReadOnlyCollection<Guid> requestedIds,
        CancellationToken cancellationToken = default)
    {
        if (requestedIds.Count == 0)
        {
            var defaults = await context.FamilyMembers.AsNoTracking()
                .Where(member => member.IsActive && !member.IsGuest)
                .OrderBy(member => member.Name)
                .Select(member => member.Id)
                .ToListAsync(cancellationToken);
            return new AttendanceResolution(defaults, [], true);
        }

        var distinct = requestedIds.Distinct().ToList();
        var active = await context.FamilyMembers.AsNoTracking()
            .Where(member => member.IsActive && distinct.Contains(member.Id))
            .Select(member => member.Id)
            .ToListAsync(cancellationToken);
        var activeSet = active.ToHashSet();
        return new AttendanceResolution(
            distinct.Where(activeSet.Contains).ToList(),
            distinct.Where(id => !activeSet.Contains(id)).ToList(),
            false);
    }

    public async Task<IReadOnlyList<Guid>> ResolveValidAsync(
        IReadOnlyCollection<Guid> requestedIds,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolveAsync(requestedIds, cancellationToken);
        if (resolution.InvalidIds.Count > 0)
            throw new InvalidAttendanceSelectionException(resolution.InvalidIds);
        return resolution.AttendeeIds;
    }
}

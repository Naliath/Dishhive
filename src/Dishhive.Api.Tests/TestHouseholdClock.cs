using Dishhive.Api.Services.Time;

namespace Dishhive.Api.Tests;

internal sealed class TestHouseholdClock(DateOnly today) : IHouseholdClock
{
    public static IHouseholdClock System { get; } = new HouseholdClock(
        TimeProvider.System, TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels"));
    public DateOnly Today => today;
    public string TimeZoneId => "Europe/Brussels";
}

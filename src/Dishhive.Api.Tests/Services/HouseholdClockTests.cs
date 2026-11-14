using Dishhive.Api.Services.Time;

namespace Dishhive.Api.Tests.Services;

public sealed class HouseholdClockTests
{
    private sealed class FixedTime(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }

    [Theory]
    [InlineData("2026-11-13T22:59:59Z", "2026-11-13")]
    [InlineData("2026-11-13T23:00:00Z", "2026-11-14")]
    [InlineData("2026-03-28T23:30:00Z", "2026-03-29")]
    [InlineData("2026-03-29T00:59:59Z", "2026-03-29")]
    [InlineData("2026-03-29T01:00:00Z", "2026-03-29")]
    [InlineData("2026-03-29T22:00:00Z", "2026-03-30")]
    [InlineData("2026-10-25T00:30:00Z", "2026-10-25")]
    [InlineData("2026-10-25T01:30:00Z", "2026-10-25")]
    [InlineData("2026-10-25T22:59:59Z", "2026-10-25")]
    [InlineData("2026-10-25T23:00:00Z", "2026-10-26")]
    public void CalendarDayUsesHouseholdMidnightAcrossBothDstTransitions(string instant, string expected)
    {
        var clock = new HouseholdClock(new FixedTime(DateTimeOffset.Parse(instant)),
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels"));
        Assert.Equal(DateOnly.Parse(expected), clock.Today);
    }

    [Fact]
    public void ConfiguredZoneCanDifferFromBothUtcAndHost()
    {
        var clock = new HouseholdClock(new FixedTime(DateTimeOffset.Parse("2026-11-13T02:00:00Z")),
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        Assert.Equal(new DateOnly(2026, 11, 12), clock.Today);
    }
}

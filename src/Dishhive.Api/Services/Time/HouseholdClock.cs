namespace Dishhive.Api.Services.Time;

public interface IHouseholdClock
{
    DateOnly Today { get; }
    string TimeZoneId { get; }
}

/// <summary>Calendar decisions use household time; persisted event timestamps remain UTC.</summary>
public sealed class HouseholdClock(TimeProvider timeProvider, TimeZoneInfo timeZone) : IHouseholdClock
{
    public string TimeZoneId => timeZone.Id;
    public DateOnly Today => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone).DateTime);
}

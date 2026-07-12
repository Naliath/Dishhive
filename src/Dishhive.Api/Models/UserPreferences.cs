using System.Text.Json.Serialization;

namespace Dishhive.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter<MeasurementSystem>))]
public enum MeasurementSystem
{
    Metric,
    Imperial
}

[JsonConverter(typeof(JsonStringEnumConverter<FirstDayOfWeek>))]
public enum FirstDayOfWeek
{
    Monday,
    Sunday
}

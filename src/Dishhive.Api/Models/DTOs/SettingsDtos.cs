using System.ComponentModel.DataAnnotations;
using Dishhive.Api.Models.Settings;

namespace Dishhive.Api.Models.DTOs;

public sealed record SettingsDto(
    MeasurementSystem MeasurementSystem,
    bool FreezyEnabled);

public sealed class UpdateMeasurementSystemDto
{
    [Required] public MeasurementSystem MeasurementSystem { get; set; }
}

public sealed class UpdateFreezyEnabledDto
{
    public bool Enabled { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.Settings;

public enum MeasurementSystem
{
    Metric = 0,
    Imperial = 1
}

/// <summary>
/// Generic key/value setting store, mirroring Freezy's <c>UserSetting</c> shape.
/// </summary>
public class UserSetting
{
    [Required]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Value { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class SettingKeys
{
    public const string MeasurementSystem = "defaults.measurement_system";
    public const string FreezyEnabled = "freezy.enabled";
}

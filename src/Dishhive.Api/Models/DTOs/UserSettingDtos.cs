using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.DTOs;

public class UserSettingDto
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpsertUserSettingDto
{
    [Required]
    [MaxLength(1000)]
    public string Value { get; set; } = string.Empty;
}

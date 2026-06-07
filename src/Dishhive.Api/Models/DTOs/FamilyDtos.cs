using System.ComponentModel.DataAnnotations;
using Dishhive.Api.Models.Family;

namespace Dishhive.Api.Models.DTOs;

public sealed record FamilyMemberDto(
    Guid Id,
    string DisplayName,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<FamilyMemberPreferenceDto> Preferences);

public sealed record FamilyMemberPreferenceDto(
    Guid Id,
    Guid FamilyMemberId,
    PreferenceKind Kind,
    string Value,
    Guid? RecipeId);

public sealed class CreateFamilyMemberDto
{
    [Required, MaxLength(100)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(500)] public string? Notes { get; set; }
}

public sealed class UpdateFamilyMemberDto
{
    [Required, MaxLength(100)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(500)] public string? Notes { get; set; }
}

public sealed class CreatePreferenceDto
{
    public PreferenceKind Kind { get; set; }
    [Required, MaxLength(200)] public string Value { get; set; } = string.Empty;
    public Guid? RecipeId { get; set; }
}

public sealed class UpdatePreferenceDto
{
    public PreferenceKind Kind { get; set; }
    [Required, MaxLength(200)] public string Value { get; set; } = string.Empty;
    public Guid? RecipeId { get; set; }
}

public sealed record GuestDto(
    Guid Id,
    string DisplayName,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed class CreateGuestDto
{
    [Required, MaxLength(100)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(500)] public string? Notes { get; set; }
}

public sealed class UpdateGuestDto
{
    [Required, MaxLength(100)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(500)] public string? Notes { get; set; }
}

public static class FamilyMappers
{
    public static FamilyMemberDto ToDto(this FamilyMember m) => new(
        m.Id, m.DisplayName, m.Notes, m.CreatedAt, m.UpdatedAt,
        m.Preferences.Select(ToDto).ToList());

    public static FamilyMemberPreferenceDto ToDto(this FamilyMemberPreference p) =>
        new(p.Id, p.FamilyMemberId, p.Kind, p.Value, p.RecipeId);

    public static GuestDto ToDto(this Guest g) =>
        new(g.Id, g.DisplayName, g.Notes, g.CreatedAt, g.UpdatedAt);
}

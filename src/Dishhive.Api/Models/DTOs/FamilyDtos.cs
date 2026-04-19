using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.DTOs;

public static class FamilyDtos
{
    public record FamilyMemberDto(
        Guid Id,
        string Name,
        bool IsGuest,
        DateOnly? GuestFrom,
        DateOnly? GuestUntil,
        List<MemberPreferenceDto> Preferences,
        List<FavoriteDishDto> FavoriteDishes,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );

    public record FamilyMemberSummaryDto(
        Guid Id,
        string Name,
        bool IsGuest,
        DateOnly? GuestUntil
    );

    public record MemberPreferenceDto(
        Guid Id,
        string PreferenceType,
        string Value,
        string? Notes,
        DateTime CreatedAt
    );

    public record FavoriteDishDto(
        Guid Id,
        Guid? RecipeId,
        string DishName,
        DateTime CreatedAt
    );

    public record CreateFamilyMemberDto(
        [Required][MaxLength(100)] string Name,
        bool IsGuest = false,
        DateOnly? GuestFrom = null,
        DateOnly? GuestUntil = null
    );

    public record UpdateFamilyMemberDto(
        [Required][MaxLength(100)] string Name,
        bool IsGuest,
        DateOnly? GuestFrom,
        DateOnly? GuestUntil
    );

    public record AddPreferenceDto(
        [Required] string PreferenceType,
        [Required][MaxLength(200)] string Value,
        [MaxLength(500)] string? Notes
    );

    public record AddFavoriteDto(
        Guid? RecipeId,
        [Required][MaxLength(200)] string DishName
    );
}

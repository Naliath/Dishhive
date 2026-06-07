using System.ComponentModel.DataAnnotations;
using Dishhive.Api.Models.History;
using Dishhive.Api.Models.Planning;

namespace Dishhive.Api.Models.DTOs;

public sealed record DishHistoryEntryDto(
    Guid Id,
    DateOnly Date,
    MealType MealType,
    Guid? RecipeId,
    string DishLabel,
    DateTime CreatedAt);

public sealed record DishFavoriteDto(
    Guid Id,
    Guid FamilyMemberId,
    Guid? RecipeId,
    string DishLabel);

public sealed class CreateFavoriteDto
{
    public Guid? RecipeId { get; set; }
    [Required, MaxLength(300)] public string DishLabel { get; set; } = string.Empty;
}

public sealed record DishFrequencyDto(string DishLabel, Guid? RecipeId, int TimesPlanned, DateOnly LastPlanned);

public static class HistoryMappers
{
    public static DishHistoryEntryDto ToDto(this DishHistoryEntry e) =>
        new(e.Id, e.Date, e.MealType, e.RecipeId, e.DishLabel, e.CreatedAt);

    public static DishFavoriteDto ToDto(this DishFavorite f) =>
        new(f.Id, f.FamilyMemberId, f.RecipeId, f.DishLabel);
}

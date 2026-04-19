using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.DTOs;

public static class WeekPlannerDtos
{
    public record WeekPlanDto(
        Guid Id,
        DateOnly WeekStartDate,
        List<PlannedMealDto> Meals,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );

    public record PlannedMealDto(
        Guid Id,
        string DayOfWeek,
        string MealType,
        Guid? RecipeId,
        string? RecipeTitle,
        string? VagueInstruction,
        bool IsFromFreezer,
        Guid? FreezerItemId,
        string? Notes,
        List<Guid> AttendeeIds,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );

    public record CreateWeekPlanDto([Required] DateOnly WeekStartDate);

    public record UpsertPlannedMealDto(
        [Required] DayOfWeek DayOfWeek,
        string MealType = "Dinner",
        Guid? RecipeId = null,
        [MaxLength(500)] string? VagueInstruction = null,
        bool IsFromFreezer = false,
        Guid? FreezerItemId = null,
        [MaxLength(500)] string? Notes = null,
        List<Guid>? AttendeeIds = null
    );
}

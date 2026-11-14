using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.DTOs;

public sealed record FreezerReservationMealDto(Guid Id, DateOnly Date, string DishName, int Quantity);
public sealed record FreezerReservationIssueDto(string ItemRef, string? Name, string? Fingerprint,
    int StockQuantity, int ReservedQuantity, int Shortage, string Issue, List<FreezerReservationMealDto> Meals);
public sealed record FreezerReconciliationDto(string State, DateTime CheckedAt, List<FreezerReservationIssueDto> Issues);

public sealed class ResolveFreezerReservationDto
{
    [Required, MaxLength(100)] public string ItemRef { get; set; } = "";
    [Range(1, int.MaxValue)] public int Quantity { get; set; }
    /// <summary>release keeps the meal; confirm acknowledges the currently displayed Freezy details.</summary>
    [Required, RegularExpression("^(release|confirm)$")] public string Action { get; set; } = "";
    [MaxLength(64)] public string? Fingerprint { get; set; }
}

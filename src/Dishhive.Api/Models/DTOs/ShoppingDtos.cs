using System.ComponentModel.DataAnnotations;
using Dishhive.Api.Models.Shopping;

namespace Dishhive.Api.Models.DTOs;

public sealed record ShoppingListDto(
    Guid Id,
    Guid? WeekPlanId,
    string Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ShoppingListItemDto> Items);

public sealed record ShoppingListItemDto(
    Guid Id,
    int Order,
    string Name,
    decimal? Quantity,
    string? Unit,
    string? Section,
    bool Checked,
    string? Note);

public sealed class CreateShoppingListDto
{
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
}

public class UpdateShoppingListItemDto
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    [MaxLength(50)] public string? Unit { get; set; }
    [MaxLength(100)] public string? Section { get; set; }
    public bool Checked { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
}

public sealed class CreateShoppingListItemDto : UpdateShoppingListItemDto
{
    public int? Order { get; set; }
}

public static class ShoppingMappers
{
    public static ShoppingListDto ToDto(this ShoppingList l) => new(
        l.Id, l.WeekPlanId, l.Title, l.CreatedAt, l.UpdatedAt,
        l.Items.OrderBy(i => i.Order).Select(ToDto).ToList());

    public static ShoppingListItemDto ToDto(this ShoppingListItem i) =>
        new(i.Id, i.Order, i.Name, i.Quantity, i.Unit, i.Section, i.Checked, i.Note);
}

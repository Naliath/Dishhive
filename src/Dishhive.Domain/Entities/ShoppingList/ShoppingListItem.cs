
namespace Dishhive.Domain.Entities.ShoppingList;

using Dishhive.Domain.Common;

/// <summary>
/// A single item on a shopping list.
/// </summary>
public class ShoppingListItem : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public double? Quantity { get; set; }
    public string? Unit { get; set; }
    public string? Category { get; set; }
    public bool Purchased { get; set; }
    public Guid ShoppingListId { get; set; }
}

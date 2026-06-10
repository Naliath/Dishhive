
namespace Dishhive.Domain.Entities.ShoppingList;

using Dishhive.Domain.Common;

/// <summary>
/// A shopping list generated from a planned week menu.
/// </summary>
public class ShoppingList : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public ICollection<ShoppingListItem> Items { get; set; } = new List<ShoppingListItem>();
}

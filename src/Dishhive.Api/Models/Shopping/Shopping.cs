using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.Shopping;

public class ShoppingList
{
    public Guid Id { get; set; }

    public Guid? WeekPlanId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<ShoppingListItem> Items { get; set; } = new();
}

public class ShoppingListItem
{
    public Guid Id { get; set; }

    public Guid ShoppingListId { get; set; }
    public ShoppingList? ShoppingList { get; set; }

    public int Order { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public decimal? Quantity { get; set; }

    [MaxLength(50)]
    public string? Unit { get; set; }

    [MaxLength(100)]
    public string? Section { get; set; }

    public bool Checked { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}

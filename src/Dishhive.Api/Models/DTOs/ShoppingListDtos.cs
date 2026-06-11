namespace Dishhive.Api.Models.DTOs;

public class ShoppingListDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<ShoppingListItemDto> Items { get; set; } = new();
    public List<ShoppingListReminderDto> Reminders { get; set; } = new();
}

/// <summary>An aggregated shopping line. Quantities are metric (display conversion is frontend).</summary>
public class ShoppingListItemDto
{
    public string Name { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public List<string> SourceRecipes { get; set; } = new();
}

/// <summary>A planned meal without a recipe — the dish still needs deciding/ingredients</summary>
public class ShoppingListReminderDto
{
    /// <summary>The underlying planned meal, so the UI can attach a recipe to it</summary>
    public Guid PlannedMealId { get; set; }

    public DateOnly Date { get; set; }
    public string Text { get; set; } = string.Empty;
}

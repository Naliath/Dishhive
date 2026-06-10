namespace Dishhive.Api.Services.Demo;

internal sealed class SeedRecipeRecord
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Servings { get; set; } = 4;
    public int? PrepTimeMinutes { get; set; }
    public int? CookTimeMinutes { get; set; }
    public int? TotalTimeMinutes { get; set; }
    public string? Category { get; set; }
    public string? Keywords { get; set; }
    public string? VideoUrl { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string SourceProvider { get; set; } = string.Empty;
    public string? SourceRawData { get; set; }
    public string? ImageContentType { get; set; }
    public string? ImageDataBase64 { get; set; }
    public List<SeedIngredientRecord> Ingredients { get; set; } = [];
    public List<SeedStepRecord> Steps { get; set; } = [];
}

internal sealed class SeedIngredientRecord
{
    public int SortOrder { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public string OriginalText { get; set; } = string.Empty;
    public decimal? OriginalQuantity { get; set; }
    public string? OriginalUnit { get; set; }
}

internal sealed class SeedStepRecord
{
    public int StepNumber { get; set; }
    public string Instruction { get; set; } = string.Empty;
}

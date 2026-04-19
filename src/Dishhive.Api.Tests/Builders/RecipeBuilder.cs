using Dishhive.Api.Models;

namespace Dishhive.Api.Tests.Builders;

/// <summary>
/// Fluent builder for creating test Recipe instances.
/// </summary>
public class RecipeBuilder
{
    private string _title = "Test Recipe";
    private string? _description = "A test recipe description.";
    private int _servings = 4;
    private string? _pictureUrl;
    private string? _sourceUrl;
    private string? _sourceName;
    private List<RecipeIngredient> _ingredients = [];
    private List<RecipeStep> _steps = [];
    private List<string> _tags = [];

    public RecipeBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public RecipeBuilder WithDescription(string? description)
    {
        _description = description;
        return this;
    }

    public RecipeBuilder WithServings(int servings)
    {
        _servings = servings;
        return this;
    }

    public RecipeBuilder WithPictureUrl(string url)
    {
        _pictureUrl = url;
        return this;
    }

    public RecipeBuilder WithSource(string url, string name)
    {
        _sourceUrl = url;
        _sourceName = name;
        return this;
    }

    public RecipeBuilder WithTags(params string[] tags)
    {
        _tags = [.. tags];
        return this;
    }

    public RecipeBuilder WithIngredients(int count)
    {
        _ingredients = Enumerable.Range(1, count)
            .Select(i => new RecipeIngredient { Name = $"Ingredient {i}", SortOrder = i })
            .ToList();
        return this;
    }

    public RecipeBuilder WithSteps(int count)
    {
        _steps = Enumerable.Range(1, count)
            .Select(i => new RecipeStep { StepNumber = i, Instruction = $"Step {i} instruction." })
            .ToList();
        return this;
    }

    public Recipe Build() => new()
    {
        Id = Guid.NewGuid(),
        Title = _title,
        Description = _description,
        Servings = _servings,
        PictureUrl = _pictureUrl,
        SourceUrl = _sourceUrl,
        SourceName = _sourceName,
        Tags = _tags,
        Ingredients = _ingredients,
        Steps = _steps,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    /// <summary>Create multiple recipes with distinct titles.</summary>
    public static List<Recipe> CreateMany(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new RecipeBuilder().WithTitle($"Recipe {i}").Build())
            .ToList();
}

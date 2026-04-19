using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// A preparation step in a recipe.
/// </summary>
public class RecipeStep
{
    public Guid Id { get; set; }

    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    public int StepNumber { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Instruction { get; set; } = string.Empty;
}

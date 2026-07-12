using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// An ordered preparation step of a recipe
/// </summary>
public class RecipeStep
{
    public Guid Id { get; set; }

    public Guid RecipeId { get; set; }

    public Recipe? Recipe { get; set; }

    public int StepNumber { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Instruction { get; set; } = string.Empty;

    /// <summary>Source instruction preserved when AI translation changes the display text.</summary>
    [MaxLength(2000)]
    public string? OriginalInstruction { get; set; }
}

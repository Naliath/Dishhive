using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// An explicit star rating (1–5) left for a recipe after it was cooked.
/// </summary>
public class DishRating
{
    public Guid Id { get; set; }

    /// <summary>The recipe being rated.</summary>
    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    /// <summary>Star rating from 1 (terrible) to 5 (excellent).</summary>
    [Range(1, 5)]
    public int Stars { get; set; }

    /// <summary>Optional free-text comment.</summary>
    [MaxLength(1000)]
    public string? Comment { get; set; }

    /// <summary>Who left the rating. Nullable — household-level rating when null.</summary>
    public Guid? FamilyMemberId { get; set; }
    public FamilyMember? FamilyMember { get; set; }

    /// <summary>Date the dish was eaten / rated.</summary>
    public DateOnly RatedOn { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

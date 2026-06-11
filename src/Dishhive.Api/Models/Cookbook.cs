using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models;

/// <summary>
/// A curated "cookbook": a named, saved recipe filter (search term + category +
/// tags). Selecting a cookbook in the UI applies its filter to the recipe library.
/// Tags are stored by name (not FK) so a cookbook never keeps an unused tag alive
/// and survives tag pool changes — a vanished tag simply matches nothing.
/// </summary>
public class Cookbook
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Title/keyword search term of the saved filter</summary>
    [MaxLength(200)]
    public string? SearchTerm { get; set; }

    /// <summary>Category of the saved filter (matches Recipe.Category)</summary>
    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>Tag names of the saved filter; recipes must carry all of them</summary>
    public List<string> Tags { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

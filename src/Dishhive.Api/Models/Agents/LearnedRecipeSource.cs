namespace Dishhive.Api.Models.Agents;

/// <summary>
/// A blueprint emitted by the recipe-import agent that allows a static, LLM-free
/// re-import of a recipe page from the same host.
///
/// One row per host. The first import to a previously-unseen host pays the LLM cost;
/// subsequent imports replay the blueprint via <c>LearnedRecipeSourceProvider</c>.
/// </summary>
public class LearnedRecipeSource
{
    public Guid Id { get; set; }

    /// <summary>Lowercase host (e.g. <c>example.com</c>). Unique.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Stable provider key, derived as <c>learned:{host}</c>. Persisted on imported recipes.</summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary><see cref="LearnedRecipeSourceStrategy"/> serialized as string.</summary>
    public string Strategy { get; set; } = string.Empty;

    /// <summary>Strategy-specific parameters as JSON.</summary>
    public string BlueprintJson { get; set; } = "{}";

    public DateTime LearnedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public int UseCount { get; set; }

    /// <summary>The original URL that triggered learning. Audit only.</summary>
    public string SourceUrl { get; set; } = string.Empty;
}

public enum LearnedRecipeSourceStrategy
{
    /// <summary>Page contains a <c>schema.org</c> Recipe in JSON-LD. No selectors needed.</summary>
    JsonLd,

    /// <summary>Page is parsed via XPath selectors stored in the blueprint.</summary>
    XPath,
}

/// <summary>
/// Strongly-typed shape of <see cref="LearnedRecipeSource.BlueprintJson"/>.
/// Versioned so future strategies can be added additively.
/// </summary>
public sealed class RecipeImportBlueprint
{
    public int Version { get; set; } = 1;
    public LearnedRecipeSourceStrategy Strategy { get; set; } = LearnedRecipeSourceStrategy.JsonLd;

    /// <summary>XPath to title (single node). Required when <see cref="Strategy"/> is XPath.</summary>
    public string? TitleXPath { get; set; }
    public string? DescriptionXPath { get; set; }
    public string? ImageXPath { get; set; }
    public string? ServingsXPath { get; set; }

    /// <summary>XPath returning multiple nodes — one per ingredient line.</summary>
    public string? IngredientsXPath { get; set; }

    /// <summary>XPath returning multiple nodes — one per step.</summary>
    public string? StepsXPath { get; set; }
}

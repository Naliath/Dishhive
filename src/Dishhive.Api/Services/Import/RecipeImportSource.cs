using Dishhive.Api.Models;

namespace Dishhive.Api.Services.Import;

/// <summary>Source of an already extracted recipe without a remote page identity.</summary>
public enum RecipeImportSource { Text, Photo }

public static class RecipeImportProvenance
{
    public const string TextProvider = "llm-text";
    public const string PhotoProvider = "llm-image";

    public static string ProviderKey(RecipeImportSource source) => source switch
    {
        RecipeImportSource.Text => TextProvider,
        RecipeImportSource.Photo => PhotoProvider,
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    public static bool CanTranslate(Recipe recipe) => recipe.SourceUrl is not null
        || recipe.SourceProvider is TextProvider or PhotoProvider;
}

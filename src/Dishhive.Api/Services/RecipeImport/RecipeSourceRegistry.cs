namespace Dishhive.Api.Services.RecipeImport;

/// <summary>
/// Resolves an <see cref="IRecipeSourceProvider"/> for a given URL. New providers
/// are added via DI registration only — no changes here are needed.
/// </summary>
public interface IRecipeSourceRegistry
{
    IRecipeSourceProvider? FindFor(Uri url);
    IReadOnlyList<IRecipeSourceProvider> All { get; }
}

public sealed class RecipeSourceRegistry : IRecipeSourceRegistry
{
    private readonly IReadOnlyList<IRecipeSourceProvider> _providers;

    public RecipeSourceRegistry(IEnumerable<IRecipeSourceProvider> providers)
    {
        _providers = providers.ToList();
    }

    public IReadOnlyList<IRecipeSourceProvider> All => _providers;

    public IRecipeSourceProvider? FindFor(Uri url) =>
        _providers.FirstOrDefault(p => p.CanHandle(url));
}

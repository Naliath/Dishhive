using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Import;

public interface IRecipeImportService
{
    /// <summary>
    /// Imports a recipe from an external URL. Re-importing a URL that was imported
    /// before updates the existing recipe instead of creating a duplicate.
    /// </summary>
    /// <exception cref="UnsupportedRecipeSourceException">No provider handles the URL</exception>
    /// <exception cref="RecipeExtractionFailedException">Page contains no recipe data</exception>
    Task<Recipe> ImportAsync(string url, CancellationToken cancellationToken = default);
}

public class RecipeImportService : IRecipeImportService
{
    private readonly HttpClient _httpClient;
    private readonly IEnumerable<IRecipeSourceProvider> _providers;
    private readonly DishhiveDbContext _context;
    private readonly ILogger<RecipeImportService> _logger;

    public RecipeImportService(
        HttpClient httpClient,
        IEnumerable<IRecipeSourceProvider> providers,
        DishhiveDbContext context,
        ILogger<RecipeImportService> logger)
    {
        _httpClient = httpClient;
        _providers = providers;
        _context = context;
        _logger = logger;
    }

    public async Task<Recipe> ImportAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new UnsupportedRecipeSourceException(url);
        }

        var provider = _providers.FirstOrDefault(p => p.CanHandle(uri))
            ?? throw new UnsupportedRecipeSourceException(url);

        _logger.LogInformation("Importing recipe from {Url} via provider {Provider}", uri, provider.Key);

        var html = await _httpClient.GetStringAsync(uri, cancellationToken);
        var imported = await provider.ExtractAsync(html, uri, cancellationToken);

        var sourceUrl = imported.SourceUrl ?? uri.AbsoluteUri;

        var recipe = await _context.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.SourceUrl == sourceUrl, cancellationToken);

        if (recipe == null)
        {
            recipe = new Recipe();
            _context.Recipes.Add(recipe);
        }
        else
        {
            _logger.LogInformation("Recipe for {SourceUrl} already exists; updating", sourceUrl);
            _context.RecipeIngredients.RemoveRange(recipe.Ingredients);
            _context.RecipeSteps.RemoveRange(recipe.Steps);
            recipe.Ingredients.Clear();
            recipe.Steps.Clear();
        }

        ApplyImportedRecipe(recipe, imported, sourceUrl, provider.Key);
        await DownloadImageAsync(recipe, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        return recipe;
    }

    /// <summary>
    /// Downloads the recipe image and stores it on the entity so recipes don't depend on
    /// expiring source URLs (signed Google Storage links on Dagelijkse Kost).
    /// Failures are logged and tolerated: the remote URL remains as fallback.
    /// </summary>
    private async Task DownloadImageAsync(Recipe recipe, CancellationToken cancellationToken)
    {
        const int maxImageBytes = 5 * 1024 * 1024;

        if (string.IsNullOrWhiteSpace(recipe.ImageUrl)
            || !Uri.TryCreate(recipe.ImageUrl, UriKind.Absolute, out var imageUri))
        {
            return;
        }

        try
        {
            using var response = await _httpClient.GetAsync(imageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType == null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Recipe image {Url} has non-image content type {ContentType}; skipping download",
                    imageUri, contentType ?? "(none)");
                return;
            }

            if (response.Content.Headers.ContentLength > maxImageBytes)
            {
                _logger.LogWarning("Recipe image {Url} exceeds {MaxBytes} bytes; skipping download", imageUri, maxImageBytes);
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0 || bytes.Length > maxImageBytes)
            {
                return;
            }

            recipe.ImageData = bytes;
            recipe.ImageContentType = contentType;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Could not download recipe image {Url}; keeping remote URL only", imageUri);
        }
    }

    private static void ApplyImportedRecipe(Recipe recipe, ImportedRecipe imported, string sourceUrl, string providerKey)
    {
        recipe.Title = imported.Title;
        recipe.Description = imported.Description;
        recipe.Servings = imported.Servings ?? 4;
        recipe.PrepTimeMinutes = imported.PrepTimeMinutes;
        recipe.CookTimeMinutes = imported.CookTimeMinutes;
        recipe.TotalTimeMinutes = imported.TotalTimeMinutes;
        recipe.Category = imported.Category;
        recipe.Keywords = imported.Keywords;
        recipe.ImageUrl = imported.ImageUrl;
        recipe.VideoUrl = imported.VideoUrl;
        recipe.SourceUrl = sourceUrl;
        recipe.SourceProvider = providerKey;
        recipe.SourceRawData = imported.RawData;

        var sortOrder = 0;
        foreach (var line in imported.IngredientLines)
        {
            var parsed = IngredientLineParser.Parse(line);
            recipe.Ingredients.Add(new RecipeIngredient
            {
                SortOrder = sortOrder++,
                Name = parsed.Name,
                Quantity = parsed.Quantity,
                Unit = parsed.Unit,
                OriginalText = parsed.OriginalText,
                OriginalQuantity = parsed.OriginalQuantity,
                OriginalUnit = parsed.OriginalUnit
            });
        }

        var stepNumber = 1;
        foreach (var step in imported.Steps)
        {
            recipe.Steps.Add(new RecipeStep
            {
                StepNumber = stepNumber++,
                Instruction = step
            });
        }
    }
}

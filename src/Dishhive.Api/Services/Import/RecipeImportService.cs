using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Facts;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Dishhive.Api.Services.Import;

public interface IRecipeImportService
{
    /// <summary>
    /// Imports a recipe from an external URL. Re-importing a URL that was imported
    /// before updates the existing recipe instead of creating a duplicate. When the
    /// structured providers can't parse the page and AI is configured, a best-effort
    /// LLM extraction is tried before giving up.
    /// </summary>
    /// <exception cref="UnsupportedRecipeSourceException">No provider handles the URL</exception>
    /// <exception cref="RecipeExtractionFailedException">Page contains no recipe data</exception>
    Task<Recipe> ImportAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches and extracts a recipe from a URL WITHOUT persisting it, for the AI
    /// planner's read-only get_recipe tool. On a URL that the structured scrapers
    /// can't parse, returns the cleaned page text instead so the model can still
    /// judge it. Applies the SSRF guard (model-chosen URL).
    /// </summary>
    Task<RecipePreview> PreviewAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only extraction result for the get_recipe tool. Either a structured recipe
/// (<see cref="Scrapable"/> = true) or, when no scraper could parse the page, the
/// cleaned <see cref="Text"/> for the model to read. <see cref="Error"/> is set when
/// the page could not even be fetched (blocked URL / network).
/// </summary>
public record RecipePreview(bool Scrapable, ImportedRecipe? Recipe, string? Text, string? Error);

public class RecipeImportService : IRecipeImportService
{
    internal const int MaxPageBytes = 4 * 1024 * 1024;

    private readonly ISafeHttpFetcher _httpFetcher;
    private readonly IEnumerable<IRecipeSourceProvider> _providers;
    private readonly DishhiveDbContext _context;
    private readonly ILlmRecipeExtractor _llmExtractor;
    private readonly RecipeFactsAssessmentService _factsQueue;
    private readonly ILogger<RecipeImportService> _logger;

    public RecipeImportService(
        ISafeHttpFetcher httpFetcher,
        IEnumerable<IRecipeSourceProvider> providers,
        DishhiveDbContext context,
        ILlmRecipeExtractor llmExtractor,
        RecipeFactsAssessmentService factsQueue,
        ILogger<RecipeImportService> logger)
    {
        _httpFetcher = httpFetcher;
        _providers = providers;
        _context = context;
        _llmExtractor = llmExtractor;
        _factsQueue = factsQueue;
        _logger = logger;
    }

    public async Task<Recipe> ImportAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new UnsupportedRecipeSourceException(url);
        }

        var initialProvider = _providers.FirstOrDefault(p => p.CanHandle(uri));
        if (initialProvider == null && !_llmExtractor.IsAvailable)
        {
            throw new UnsupportedRecipeSourceException(url);
        }

        FetchedHttpResource resource;
        try
        {
            resource = await _httpFetcher.GetAsync(uri.AbsoluteUri, MaxPageBytes, cancellationToken);
        }
        catch (SafeHttpFetchException ex) when (ex.UnsafeUrl)
        {
            throw new UnsupportedRecipeSourceException(url);
        }

        uri = resource.FinalUri;
        EnsureHtmlContent(resource, uri);
        var html = resource.ReadText();
        var provider = _providers.FirstOrDefault(p => p.CanHandle(uri));
        if (provider == null && !_llmExtractor.IsAvailable)
        {
            throw new UnsupportedRecipeSourceException(uri.AbsoluteUri);
        }

        ImportedRecipe imported;
        string providerKey;
        if (provider != null)
        {
            _logger.LogInformation("Importing recipe from {Url} via provider {Provider}", uri, provider.Key);
            try
            {
                imported = await provider.ExtractAsync(html, uri, cancellationToken);
                providerKey = provider.Key;
            }
            catch (RecipeExtractionFailedException) when (_llmExtractor.IsAvailable)
            {
                imported = await ExtractWithLlmAsync(html, uri, cancellationToken);
                providerKey = "llm";
            }
        }
        else
        {
            // No structured provider handles this site, but AI is configured — let the LLM read it
            imported = await ExtractWithLlmAsync(html, uri, cancellationToken);
            providerKey = "llm";
        }

        var sourceUrl = imported.SourceUrl ?? uri.AbsoluteUri;

        var recipe = await _context.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.SourceUrl == sourceUrl, cancellationToken);

        var isReimport = recipe != null;
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

        ApplyImportedRecipe(recipe, imported, sourceUrl, providerKey);
        await RecipeImageDownloader.TryDownloadAsync(_httpFetcher, recipe, _logger, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        // Fire-and-forget dietary-facts assessment (no-op when AI is unconfigured).
        // A re-import replaced the ingredient list, so even user-confirmed facts are
        // stale then and get overwritten by the fresh assessment.
        _factsQueue.TryEnqueue(recipe.Id, overwriteUserConfirmed: isReimport);
        return recipe;
    }

    /// <summary>Runs the LLM extractor, mapping a no-recipe result to the standard failure</summary>
    private async Task<ImportedRecipe> ExtractWithLlmAsync(string html, Uri uri, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Structured extraction unavailable for {Url}; trying LLM extraction", uri);
        return await _llmExtractor.ExtractAsync(html, uri, cancellationToken)
            ?? throw new RecipeExtractionFailedException(
                $"No recipe data found at '{uri}'. The page may not be a recipe, or the site is not supported.");
    }

    public async Task<RecipePreview> PreviewAsync(string url, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var fetchStopwatch = Stopwatch.StartNew();
        FetchedHttpResource resource;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            resource = await _httpFetcher.GetAsync(url, MaxPageBytes, cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogInformation(ex, "Recipe preview could not fetch {Url} after {ElapsedMs}ms",
                url, fetchStopwatch.ElapsedMilliseconds);
            return new RecipePreview(false, null, null, ex.Message);
        }
        var uri = resource.FinalUri;
        try
        {
            EnsureHtmlContent(resource, uri);
        }
        catch (RecipeExtractionFailedException ex)
        {
            return new RecipePreview(false, null, null, ex.Message);
        }
        var html = resource.ReadText();
        var fetchElapsedMs = fetchStopwatch.ElapsedMilliseconds;

        // Try the structured providers (no persistence); on failure, hand back the page
        // text so the model can still read the page itself.
        var provider = _providers.FirstOrDefault(p => p.CanHandle(uri));
        if (provider != null)
        {
            var extractStopwatch = Stopwatch.StartNew();
            try
            {
                var imported = await provider.ExtractAsync(html, uri, cancellationToken);
                _logger.LogInformation(
                    "Recipe preview for {Url}: fetch={FetchMs}ms, extract={ExtractMs}ms, total={TotalMs}ms",
                    uri, fetchElapsedMs, extractStopwatch.ElapsedMilliseconds, stopwatch.ElapsedMilliseconds);
                return new RecipePreview(true, imported, null, null);
            }
            catch (RecipeExtractionFailedException)
            {
                // fall through to text
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // sidecar unreachable etc. — fall through to text
                _logger.LogInformation(ex, "Recipe preview extraction failed for {Url} after {ElapsedMs}ms",
                    uri, extractStopwatch.ElapsedMilliseconds);
            }
        }

        _logger.LogInformation(
            "Recipe preview for {Url}: not scrapable, returning page text; fetch={FetchMs}ms, total={TotalMs}ms",
            uri, fetchElapsedMs, stopwatch.ElapsedMilliseconds);
        return new RecipePreview(false, null, HtmlText.ToPlainText(html), null);
    }

    /// <summary>Maps an extracted recipe onto the entity (shared with file import)</summary>
    internal static void ApplyImportedRecipe(Recipe recipe, ImportedRecipe imported, string? sourceUrl, string providerKey)
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

    private static void EnsureHtmlContent(FetchedHttpResource resource, Uri uri)
    {
        if (resource.ContentType != null
            && !resource.ContentType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
            && !resource.ContentType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
        {
            throw new RecipeExtractionFailedException(
                $"'{uri}' returned '{resource.ContentType}' instead of an HTML page.");
        }
    }
}

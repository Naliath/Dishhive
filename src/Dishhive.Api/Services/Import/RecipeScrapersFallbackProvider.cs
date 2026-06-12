using System.Text.RegularExpressions;

namespace Dishhive.Api.Services.Import;

/// <summary>
/// Catch-all import provider backed by the recipe-scrapers sidecar container.
/// Registered after the dedicated providers, so a site with its own C# provider
/// (e.g. <see cref="DagelijkseKostProvider"/>) never reaches this one — the sidecar
/// only handles sites Dishhive has no dedicated implementation for.
/// Disabled entirely while RecipeScrapers:BaseUrl is unset.
/// </summary>
public partial class RecipeScrapersFallbackProvider(
    IRecipeScrapersClient client,
    ILogger<RecipeScrapersFallbackProvider> logger) : IRecipeSourceProvider
{
    [GeneratedRegex(@"\d+")]
    private static partial Regex FirstNumberRegex();

    public string Key => "recipe-scrapers";

    public bool CanHandle(Uri url)
    {
        return client.IsConfigured
            && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
    }

    public async Task<ImportedRecipe> ExtractAsync(string html, Uri sourceUrl, CancellationToken cancellationToken = default)
    {
        ScrapedRecipe? scraped;
        try
        {
            scraped = await client.ScrapeAsync(html, sourceUrl, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Recipe scraper sidecar unreachable at {BaseUrl}", client.BaseUrl);
            throw new RecipeExtractionFailedException(
                "The recipe scraper service could not be reached. Check that the scraper container is running.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogWarning(ex, "Recipe scraper sidecar returned an unexpected payload for {Url}", sourceUrl);
            throw new RecipeExtractionFailedException(
                $"The recipe scraper service returned an unexpected response for '{sourceUrl}'.");
        }

        if (scraped == null)
        {
            throw new RecipeExtractionFailedException(
                $"No recipe data found at '{sourceUrl}'. The page may not be a recipe, or the site is not supported.");
        }

        return new ImportedRecipe
        {
            Title = scraped.Title,
            Description = scraped.Description,
            IngredientLines = scraped.Ingredients,
            Steps = scraped.Instructions,
            Servings = ParseServings(scraped.Yields),
            ImageUrl = scraped.Image,
            SourceUrl = scraped.CanonicalUrl ?? sourceUrl.AbsoluteUri,
            PrepTimeMinutes = scraped.PrepTimeMinutes,
            CookTimeMinutes = scraped.CookTimeMinutes,
            TotalTimeMinutes = scraped.TotalTimeMinutes,
            Category = scraped.Category,
            Keywords = scraped.Keywords.Count > 0 ? string.Join(", ", scraped.Keywords) : null,
            RawData = scraped.Raw
        };
    }

    /// <summary>recipe-scrapers yields are strings like "4 servings" or "12 items"</summary>
    private static int? ParseServings(string? yields)
    {
        if (string.IsNullOrWhiteSpace(yields))
        {
            return null;
        }

        var match = FirstNumberRegex().Match(yields);
        return match.Success && int.TryParse(match.Value, out var servings) ? servings : null;
    }
}

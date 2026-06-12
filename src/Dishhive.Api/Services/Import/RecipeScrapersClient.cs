using System.Text.Json;

namespace Dishhive.Api.Services.Import;

/// <summary>
/// Client for the recipe-scrapers sidecar container (src/dishhive-scraper), which wraps
/// the Python recipe-scrapers library behind HTTP. Disabled when RecipeScrapers:BaseUrl
/// is empty. See docs/plans/RECIPE_SCRAPERS_ADOPTION_PLAN.md.
/// </summary>
public interface IRecipeScrapersClient
{
    bool IsConfigured { get; }

    string? BaseUrl { get; }

    /// <summary>Health probe; returns the installed package version or null when unreachable</summary>
    Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Installed vs latest PyPI version; null when the sidecar is unreachable</summary>
    Task<ScraperVersionInfo?> GetVersionInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the sidecar to install the given package version (null = latest) and restart.
    /// The sidecar is briefly unavailable afterwards while the container restarts.
    /// </summary>
    Task<ScraperUpdateResult> RequestUpdateAsync(string? version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts a recipe from the given page HTML. Returns null when the sidecar found
    /// no recipe in the page; throws <see cref="HttpRequestException"/> when the sidecar
    /// itself is unreachable or fails.
    /// </summary>
    Task<ScrapedRecipe?> ScrapeAsync(string html, Uri sourceUrl, CancellationToken cancellationToken = default);
}

public record ScraperVersionInfo(string InstalledVersion, string? LatestVersion, bool UpdateAvailable);

public record ScraperUpdateResult(bool Accepted, string? Version, string? Error);

/// <summary>Wire format of the sidecar's POST /scrape response</summary>
public record ScrapedRecipe
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Ingredients { get; init; } = [];
    public IReadOnlyList<string> Instructions { get; init; } = [];
    public string? Yields { get; init; }
    public string? Image { get; init; }
    public int? PrepTimeMinutes { get; init; }
    public int? CookTimeMinutes { get; init; }
    public int? TotalTimeMinutes { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public string? CanonicalUrl { get; init; }
    public string? Raw { get; init; }
}

public class RecipeScrapersClient : IRecipeScrapersClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<RecipeScrapersClient> _logger;

    public RecipeScrapersClient(HttpClient httpClient, ILogger<RecipeScrapersClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public bool IsConfigured => _httpClient.BaseAddress != null;

    public string? BaseUrl => _httpClient.BaseAddress?.ToString().TrimEnd('/');

    public async Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            var health = await _httpClient.GetFromJsonAsync<HealthDto>("healthz", JsonOptions, cts.Token);
            return health?.Version;
        }
        catch
        {
            return null;
        }
    }

    public async Task<ScraperVersionInfo?> GetVersionInfoAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15)); // includes the sidecar's PyPI lookup
            var info = await _httpClient.GetFromJsonAsync<VersionDto>("version", JsonOptions, cts.Token);
            if (info?.InstalledVersion == null)
            {
                return null;
            }

            return new ScraperVersionInfo(info.InstalledVersion, info.LatestVersion, info.UpdateAvailable);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not get version info from the scraper sidecar at {BaseUrl}", BaseUrl);
            return null;
        }
    }

    public async Task<ScraperUpdateResult> RequestUpdateAsync(string? version, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new ScraperUpdateResult(false, null, "The scraper sidecar is not configured.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(5)); // pip install can be slow
            using var response = await _httpClient.PostAsJsonAsync("update", new { version }, JsonOptions, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var problem = await response.Content.ReadAsStringAsync(cts.Token);
                _logger.LogWarning("Scraper update failed ({StatusCode}): {Detail}", response.StatusCode, problem);
                return new ScraperUpdateResult(false, null, $"Update failed ({(int)response.StatusCode}).");
            }

            var result = await response.Content.ReadFromJsonAsync<UpdateResponseDto>(JsonOptions, cts.Token);
            return new ScraperUpdateResult(true, result?.Version, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Could not reach the scraper sidecar at {BaseUrl} for update", BaseUrl);
            return new ScraperUpdateResult(false, null, "The scraper service could not be reached.");
        }
    }

    public async Task<ScrapedRecipe?> ScrapeAsync(string html, Uri sourceUrl, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        using var response = await _httpClient.PostAsJsonAsync(
            "scrape", new { html, url = sourceUrl.AbsoluteUri }, JsonOptions, cts.Token);

        // 422 is the sidecar's "page contains no recipe" answer, not a transport failure
        if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ScrapedRecipe>(JsonOptions, cts.Token);
    }

    private sealed record HealthDto(string? Status, string? Package, string? Version);

    private sealed record VersionDto(string? InstalledVersion, string? LatestVersion, bool UpdateAvailable);

    private sealed record UpdateResponseDto(string? Status, string? Version);
}

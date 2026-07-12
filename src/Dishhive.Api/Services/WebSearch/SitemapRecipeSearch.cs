using Dishhive.Api.Services.Import;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Dishhive.Api.Services.WebSearch;

public interface ISitemapRecipeSearch
{
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query, string site, int count, CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministic source-specific fallback when a metasearch engine returns no hits.
/// It discovers sitemap URLs through robots.txt, indexes a bounded set of recipe/post
/// sitemaps, and ranks URL slugs locally. Page content is still verified later by the
/// normal safe recipe preview path; sitemap hits are never trusted as recipes by themselves.
/// </summary>
public sealed partial class SitemapRecipeSearch(
    ISafeHttpFetcher fetcher,
    ILogger<SitemapRecipeSearch> logger) : ISitemapRecipeSearch
{
    private const int MaxSitemapBytes = 8 * 1024 * 1024;
    private const int MaxChildSitemaps = 12;
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<Uri>>> _indexes =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query, string site, int count, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(site) || count <= 0)
        {
            return [];
        }

        var host = NormalizeHost(site);
        var urls = await _indexes.GetOrAdd(host, _ => BuildIndexAsync(host, cancellationToken));
        var terms = SearchTerms(query);
        if (terms.Count == 0)
        {
            return [];
        }

        return urls
            .Select((url, index) => new { Url = url, Index = index, Text = Normalize(url.AbsolutePath) })
            .Where(candidate => RecipePageClassifier.IsPotentialRecipeUri(candidate.Url.AbsoluteUri))
            .Select(candidate => new
            {
                candidate.Url,
                candidate.Index,
                Score = terms.Sum(term => candidate.Text.Contains(term.Key, StringComparison.Ordinal) ? term.Value : 0)
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Index)
            .Take(count)
            .Select(candidate => new WebSearchResult(
                TitleFromUrl(candidate.Url), candidate.Url.AbsoluteUri, "Found in the source sitemap"))
            .ToList();
    }

    private async Task<IReadOnlyList<Uri>> BuildIndexAsync(string host, CancellationToken cancellationToken)
    {
        var roots = new List<Uri>();
        try
        {
            var robots = await fetcher.GetAsync($"https://{host}/robots.txt", 256 * 1024, cancellationToken);
            roots.AddRange(robots.ReadText().Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("Sitemap:", StringComparison.OrdinalIgnoreCase))
                .Select(line => line["Sitemap:".Length..].Trim())
                .Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
                .Where(uri => uri != null)
                .Select(uri => uri!));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogInformation(ex, "Could not read robots.txt for sitemap fallback on {Host}", host);
        }

        roots.Add(new Uri($"https://{host}/sitemap.xml"));
        roots.Add(new Uri($"https://{host}/sitemap_index.xml"));
        roots.Add(new Uri($"https://{host}/wp-sitemap.xml"));

        foreach (var root in roots.DistinctBy(uri => uri.AbsoluteUri))
        {
            try
            {
                var urls = await ReadSitemapAsync(root, host, depth: 0, cancellationToken);
                if (urls.Count > 0)
                {
                    logger.LogInformation("Sitemap fallback indexed {Count} URLs for {Host}", urls.Count, host);
                    return urls;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or XmlException)
            {
                logger.LogDebug(ex, "Sitemap candidate {Sitemap} could not be indexed", root);
            }
        }

        return [];
    }

    private async Task<IReadOnlyList<Uri>> ReadSitemapAsync(
        Uri sitemap, string expectedHost, int depth, CancellationToken cancellationToken)
    {
        if (depth > 2 || !HostMatches(sitemap, expectedHost))
        {
            return [];
        }

        var resource = await fetcher.GetAsync(sitemap.AbsoluteUri, MaxSitemapBytes, cancellationToken);
        using var stringReader = new StringReader(resource.ReadText());
        using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var document = XDocument.Load(xmlReader);
        var rootName = document.Root?.Name.LocalName;
        var locations = document.Root?.Elements()
            .Select(entry => entry.Elements().FirstOrDefault(element => element.Name.LocalName == "loc"))
            .OfType<XElement>()
            .Select(element => Uri.TryCreate(element.Value.Trim(), UriKind.Absolute, out var uri) ? uri : null)
            .Where(uri => uri != null && HostMatches(uri, expectedHost))
            .Select(uri => uri!)
            .ToList() ?? [];

        if (rootName == "urlset")
        {
            return locations;
        }
        if (rootName != "sitemapindex")
        {
            return [];
        }

        var children = locations
            .Take(MaxChildSitemaps)
            .ToList();
        var results = await Task.WhenAll(children.Select(child =>
            ReadSitemapAsync(child, expectedHost, depth + 1, cancellationToken)));
        return results.SelectMany(result => result).DistinctBy(uri => uri.AbsoluteUri).ToList();
    }

    private static IReadOnlyDictionary<string, int> SearchTerms(string query)
    {
        return Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(term => term, _ => 5, StringComparer.Ordinal);
    }

    private static string Normalize(string value)
    {
        var decomposed = Uri.UnescapeDataString(value).Normalize(NormalizationForm.FormD);
        return new string(decomposed
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ')
            .ToArray());
    }

    private static string TitleFromUrl(Uri uri)
    {
        var slug = uri.Segments.LastOrDefault()?.Trim('/') ?? uri.Host;
        var title = Uri.UnescapeDataString(slug).Replace('-', ' ').Replace('_', ' ').Trim();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(title);
    }

    private static string NormalizeHost(string site)
    {
        var host = Uri.TryCreate(site, UriKind.Absolute, out var uri) ? uri.Host : site.Trim();
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    private static bool HostMatches(Uri uri, string expectedHost)
    {
        var actual = NormalizeHost(uri.Host);
        return string.Equals(actual, expectedHost, StringComparison.OrdinalIgnoreCase);
    }
}

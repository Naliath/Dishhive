using Dishhive.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Import;

/// <summary>A recipe source the app knows about, for the @[Source] picker</summary>
public record RecipeSource(string Name, string Host);

/// <summary>
/// Lists the recipe sources the app knows about: the dedicated import providers
/// (with friendly names) unioned with the distinct hosts the library has already been
/// imported from. Drives the week-planner's @[Source] autocomplete and resolves a
/// referenced source name back to a host for the AI search tool.
/// </summary>
public class RecipeSourceCatalog(DishhiveDbContext context, IEnumerable<IRecipeSourceProvider> providers)
{
    /// <summary>All known sources (provider-defined ∪ previously-imported hosts), by name</summary>
    public async Task<IReadOnlyList<RecipeSource>> ListAsync(CancellationToken cancellationToken = default)
    {
        var byHost = new Dictionary<string, RecipeSource>(StringComparer.OrdinalIgnoreCase);

        // Dedicated providers first — they carry the nice display name
        foreach (var provider in providers)
        {
            foreach (var host in provider.Hosts)
            {
                var normalized = NormalizeHost(host);
                if (normalized != null)
                {
                    byHost.TryAdd(normalized, new RecipeSource(provider.DisplayName, normalized));
                }
            }
        }

        // Hosts already present in the recipe store (e.g. sites imported via the sidecar
        // or LLM extraction), named after the host when no provider claims them
        var sourceUrls = await context.Recipes
            .AsNoTracking()
            .Where(r => r.SourceUrl != null)
            .Select(r => r.SourceUrl!)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var url in sourceUrls)
        {
            var host = HostOf(url);
            if (host != null && !byHost.ContainsKey(host))
            {
                byHost[host] = new RecipeSource(host, host);
            }
        }

        return byHost.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Resolves a @[Source] reference to a host: matches a known source by name,
    /// otherwise accepts a bare domain typed by hand (e.g. "@[example.com]").
    /// Returns null when neither applies.
    /// </summary>
    public async Task<string?> ResolveHostAsync(string nameOrDomain, CancellationToken cancellationToken = default)
    {
        var trimmed = nameOrDomain.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var sources = await ListAsync(cancellationToken);
        var byName = sources.FirstOrDefault(s => s.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (byName != null)
        {
            return byName.Host;
        }

        // A hand-typed domain (contains a dot, no spaces) is taken as its own host
        return trimmed.Contains('.') && !trimmed.Contains(' ') ? NormalizeHost(trimmed) : null;
    }

    private static string? HostOf(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? NormalizeHost(uri.Host) : null;

    private static string? NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var normalized = host.Trim().ToLowerInvariant();
        return normalized.StartsWith("www.") ? normalized[4..] : normalized;
    }
}

namespace Dishhive.Api.Services.WebSearch;

/// <summary>
/// Conservative page classification, with known source routes where available. Search results remain
/// candidates until the import preview confirms structured recipe data.
/// </summary>
public static class RecipePageClassifier
{
    private static readonly string[] NonContentSegments =
        ["/category/", "/tag/", "/author/", "/search/", "/feed/"];

    public static bool IsPotentialRecipeUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }
        // This provider publishes individual recipes under /gerechten/. Its sitemap
        // also contains cooking questions, magazines and theme lists. Those pages
        // consumed the bounded research allowance without containing a recipe.
        // This is route provenance, never a title/dietary-language heuristic.
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        if (host.Equals("dagelijksekost.vrt.be", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.StartsWith("/gerechten/", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.Trim('/').Split('/').Length == 2;
        return !NonContentSegments.Any(segment =>
            uri.AbsolutePath.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }
}

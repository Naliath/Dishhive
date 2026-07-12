namespace Dishhive.Api.Services.WebSearch;

/// <summary>
/// Language-neutral, conservative page classification. Search results remain
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
        return !NonContentSegments.Any(segment =>
            uri.AbsolutePath.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }
}

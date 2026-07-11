using Dishhive.Api.Services.Import;

namespace Dishhive.Api.Tests.Mocks;

public sealed class AllowAllPublicUrlValidator : IPublicUrlValidator
{
    public Task<(bool Ok, Uri? Uri, string? Error)> ValidateAsync(
        string? url,
        CancellationToken cancellationToken = default)
    {
        var ok = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        return Task.FromResult(ok
            ? (true, uri, (string?)null)
            : (false, (Uri?)null, "Only absolute http(s) URLs are allowed."));
    }
}

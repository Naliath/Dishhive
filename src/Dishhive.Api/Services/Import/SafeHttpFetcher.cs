using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Dishhive.Api.Services.Import;

public interface IPublicUrlValidator
{
    Task<(bool Ok, Uri? Uri, string? Error)> ValidateAsync(
        string? url,
        CancellationToken cancellationToken = default);
}

public sealed class PublicUrlValidator : IPublicUrlValidator
{
    public Task<(bool Ok, Uri? Uri, string? Error)> ValidateAsync(
        string? url,
        CancellationToken cancellationToken = default)
        => UrlGuard.ValidateAsync(url, cancellationToken);
}

public interface ISafeHttpFetcher
{
    Task<FetchedHttpResource> GetAsync(
        string url,
        int maxBytes,
        CancellationToken cancellationToken = default);
}

public sealed record FetchedHttpResource(
    Uri FinalUri,
    string? ContentType,
    string? CharacterSet,
    byte[] Content)
{
    public string ReadText()
    {
        Encoding encoding;
        try
        {
            encoding = string.IsNullOrWhiteSpace(CharacterSet)
                ? Encoding.UTF8
                : Encoding.GetEncoding(CharacterSet.Trim('"'));
        }
        catch (ArgumentException)
        {
            encoding = Encoding.UTF8;
        }

        return encoding.GetString(Content);
    }
}

public class SafeHttpFetchException(string message, bool unsafeUrl = false, Exception? innerException = null)
    : HttpRequestException(message, innerException)
{
    public bool UnsafeUrl { get; } = unsafeUrl;
}

/// <summary>
/// Fetches a bounded public HTTP(S) resource. Redirects are followed manually so each
/// destination is validated; the production handler additionally resolves and connects
/// only to public addresses, closing the DNS validation/connection gap.
/// </summary>
public sealed class SafeHttpFetcher(
    HttpClient httpClient,
    IPublicUrlValidator urlValidator) : ISafeHttpFetcher
{
    private const int MaxRedirects = 5;

    public async Task<FetchedHttpResource> GetAsync(
        string url,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        var current = url;
        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            var (ok, uri, error) = await urlValidator.ValidateAsync(current, cancellationToken);
            if (!ok || uri == null)
            {
                throw new SafeHttpFetchException(error ?? "The URL is not a public HTTP(S) resource.", unsafeUrl: true);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (redirect == MaxRedirects)
                {
                    throw new SafeHttpFetchException($"Too many redirects while fetching '{url}'.");
                }

                var location = response.Headers.Location
                    ?? throw new SafeHttpFetchException($"Redirect from '{uri}' did not include a destination.");
                current = location.IsAbsoluteUri ? location.AbsoluteUri : new Uri(uri, location).AbsoluteUri;
                continue;
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 0 and var contentLength
                && contentLength > maxBytes)
            {
                throw new SafeHttpFetchException($"The response exceeds the {maxBytes} byte limit.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var buffer = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
            var chunk = new byte[16 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(chunk, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (buffer.Length + read > maxBytes)
                {
                    throw new SafeHttpFetchException($"The response exceeds the {maxBytes} byte limit.");
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }

            return new FetchedHttpResource(
                uri,
                response.Content.Headers.ContentType?.MediaType,
                response.Content.Headers.ContentType?.CharSet,
                buffer.ToArray());
        }

        throw new SafeHttpFetchException($"Too many redirects while fetching '{url}'.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;
}

public static class PublicHttpMessageHandler
{
    public static SocketsHttpHandler Create() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        UseProxy = false,
        ConnectCallback = ConnectToPublicAddressAsync
    };

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = IPAddress.TryParse(endpoint.Host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken);
        var publicAddresses = addresses.Where(address => !UrlGuard.IsBlocked(address)).ToList();
        if (publicAddresses.Count == 0)
        {
            throw new HttpRequestException($"Host '{endpoint.Host}' does not resolve to a public address.");
        }

        Exception? lastError = null;
        foreach (var address in publicAddresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                lastError = ex;
                if (ex is OperationCanceledException)
                {
                    throw;
                }
            }
        }

        throw new HttpRequestException($"Could not connect to public host '{endpoint.Host}'.", lastError);
    }
}

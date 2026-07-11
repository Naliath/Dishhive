using System.Net;
using System.Net.Sockets;

namespace Dishhive.Api.Services;

/// <summary>
/// SSRF guard for URLs the AI planner chooses (search hits it asks to fetch,
/// recipes it imports). The model can propose arbitrary URLs, so before any
/// outbound fetch we require http/https and reject hosts that resolve to
/// loopback, private or link-local addresses — a page on the model's say-so
/// must not be able to reach the container's own network.
/// </summary>
public static class UrlGuard
{
    /// <summary>
    /// Validates a URL for a model-driven fetch. Returns false with a reason when
    /// the scheme is not http/https, the host cannot be resolved, or it resolves
    /// to a non-public address.
    /// </summary>
    public static async Task<(bool Ok, Uri? Uri, string? Error)> ValidateAsync(
        string? url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return (false, null, "Only absolute http(s) URLs are allowed.");
        }

        IPAddress[] addresses;
        try
        {
            // A literal IP host resolves to itself; a name is resolved via DNS
            addresses = IPAddress.TryParse(uri.Host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return (false, null, $"Host '{uri.Host}' could not be resolved.");
        }

        if (addresses.Length == 0 || addresses.Any(IsBlocked))
        {
            return (false, null, $"Host '{uri.Host}' resolves to a non-public address.");
        }

        return (true, uri, null);
    }

    /// <summary>Whether an address is loopback, private, link-local or otherwise not publicly routable</summary>
    internal static bool IsBlocked(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10                              // 10.0.0.0/8
                || b[0] == 127                             // 127.0.0.0/8
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // carrier-grade NAT
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)              // 192.168.0.0/16
                || (b[0] == 192 && b[1] == 0)                // IETF protocol/reserved
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19)) // benchmark networks
                || (b[0] == 198 && b[1] == 51 && b[2] == 100) // documentation
                || (b[0] == 203 && b[1] == 0 && b[2] == 113)  // documentation
                || (b[0] == 169 && b[1] == 254)              // 169.254.0.0/16 link-local
                || b[0] == 0                                 // 0.0.0.0/8
                || b[0] >= 224;                              // multicast / reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                return IsBlocked(address.MapToIPv4());
            }

            return address.Equals(IPAddress.IPv6Any)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6UniqueLocal
                || address.IsIPv6Multicast;
        }

        return true;
    }
}

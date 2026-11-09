using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using PulseWatch.Application.Probing;
using PulseWatch.Application.Security;

namespace PulseWatch.Infrastructure.Probing;

/// <summary>
/// SSRF layer 3: the probe's connection step. It resolves the host itself, rejects the request if ANY
/// answer is blocked, and opens the socket to one of the addresses it just checked. Because the address
/// checked is the address connected to, DNS can't swap in an internal address between check and use
/// (DNS rebinding). TLS still validates the certificate against the real host name, because the request
/// URI keeps the host name; only the TCP connection is pinned.
/// </summary>
public static class SafeConnect
{
    /// <summary>The primary handler for the probe HttpClient.</summary>
    public static SocketsHttpHandler CreateHandler(IDnsResolver dnsResolver, IAddressPolicy addressPolicy) => new()
    {
        AllowAutoRedirect = false,          // redirects are followed manually, re-validating every hop
        UseCookies = false,                 // never store or send cookies
        UseProxy = false,                   // an environment proxy would bypass the address check
        AutomaticDecompression = DecompressionMethods.None,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(15),
        MaxResponseHeadersLength = 64,      // KB
        ConnectCallback = (context, cancellationToken) =>
            ConnectAsync(dnsResolver, addressPolicy, context.DnsEndPoint, cancellationToken),
    };

    public static async ValueTask<Stream> ConnectAsync(
        IDnsResolver dnsResolver, IAddressPolicy addressPolicy, DnsEndPoint endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dnsResolver);
        ArgumentNullException.ThrowIfNull(addressPolicy);
        ArgumentNullException.ThrowIfNull(endpoint);

        var addresses = await ResolveAsync(dnsResolver, endpoint.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Any(address => !addressPolicy.IsAllowed(address)))
        {
            throw new DestinationBlockedException();
        }

        ExceptionDispatchInfo? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                lastError = ExceptionDispatchInfo.Capture(ex);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        lastError!.Throw();
        throw new UnreachableException();
    }

    private static async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        IDnsResolver dnsResolver, string host, CancellationToken cancellationToken)
    {
        var trimmed = host.Trim('[', ']');
        if (IPAddress.TryParse(trimmed, out var literal))
        {
            return [literal];
        }

        IReadOnlyList<IPAddress> addresses;
        try
        {
            addresses = await dnsResolver.ResolveAsync(trimmed, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DnsResolutionFailedException("The host name could not be resolved.", ex);
        }

        return addresses.Count > 0 ? addresses : throw new DnsResolutionFailedException();
    }

    private sealed class UnreachableException : InvalidOperationException;
}

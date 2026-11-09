using System.Net;
using PulseWatch.Application.Security;

namespace PulseWatch.Infrastructure.Network;

/// <summary>Resolves host names with the operating system resolver.</summary>
internal sealed class SystemDnsResolver : IDnsResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
        await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
}

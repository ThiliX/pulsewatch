using System.Net;

namespace PulseWatch.Application.Security;

/// <summary>Resolves a host name to its A/AAAA addresses. Faked in tests so they never touch real DNS.</summary>
public interface IDnsResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}

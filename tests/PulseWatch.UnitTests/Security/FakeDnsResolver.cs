using System.Net;
using PulseWatch.Application.Security;

namespace PulseWatch.UnitTests.Security;

/// <summary>DNS answers chosen by the test. Unknown hosts fail like a real NXDOMAIN.</summary>
internal sealed class FakeDnsResolver : IDnsResolver
{
    private readonly Dictionary<string, Func<IReadOnlyList<IPAddress>>> _answers = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Queries { get; } = [];

    public FakeDnsResolver Answer(string host, params string[] addresses)
    {
        _answers[host] = () => addresses.Select(IPAddress.Parse).ToArray();
        return this;
    }

    public FakeDnsResolver Throw(string host, Exception exception)
    {
        _answers[host] = () => throw exception;
        return this;
    }

    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        Queries.Add(host);
        return _answers.TryGetValue(host, out var answer)
            ? Task.FromResult(answer())
            : throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound);
    }
}

using System.Net;
using System.Net.Sockets;
using System.Text;
using PulseWatch.Application.Security;

namespace PulseWatch.IntegrationTests.Infrastructure;

/// <summary>
/// A tiny HTTP/1.1 server on 127.0.0.1 whose status code the test can switch at any time,
/// to drive a monitor between healthy and failing.
/// </summary>
internal sealed class SwitchableHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private volatile int _status = 200;

    public SwitchableHttpServer()
    {
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int Status
    {
        get => _status;
        set => _status = value;
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => HandleAsync(client));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[8192];
                var received = new StringBuilder();
                while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer, _stop.Token);
                    if (read == 0)
                    {
                        return;
                    }

                    received.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                var status = Status;
                var response = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status} {(status < 400 ? "OK" : "Error")}\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
                await stream.WriteAsync(response, _stop.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }
}

/// <summary>Allows loopback in addition to public addresses. Registered ONLY in tests that probe a local server.</summary>
internal sealed class LoopbackAllowedPolicy : IAddressPolicy
{
    public bool IsAllowed(IPAddress address) => IPAddress.IsLoopback(address) || PublicAddressPolicy.IsPublic(address);
}

/// <summary>Resolves every host name to 127.0.0.1, so "target.test" reaches the local server.</summary>
internal sealed class LoopbackDns : IDnsResolver
{
    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Loopback]);
}

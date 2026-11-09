using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PulseWatch.Application.Options;
using PulseWatch.Application.Probing;
using PulseWatch.Application.Security;
using PulseWatch.Infrastructure.Probing;
using PulseWatch.UnitTests.Security;

namespace PulseWatch.UnitTests.Probing;

/// <summary>An HttpMessageHandler whose behaviour each test scripts.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return respond(request, cancellationToken);
    }

    public static HttpResponseMessage Status(int code, string? location = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)code) { Content = new ByteArrayContent([]) };
        if (location is not null)
        {
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        }

        return response;
    }
}

/// <summary>A response body that never ends, counting how much was read.</summary>
internal sealed class EndlessStream : Stream
{
    public long BytesRead { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => BytesRead; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        BytesRead += count;
        return count;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        BytesRead += buffer.Length;
        return ValueTask.FromResult(buffer.Length);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>A response body that sends nothing until cancelled.</summary>
internal sealed class StallingStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => 0; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Allows loopback in addition to public addresses — ONLY for tests that talk to a local listener.</summary>
internal sealed class LoopbackAllowedPolicy : IAddressPolicy
{
    public bool IsAllowed(IPAddress address) => IPAddress.IsLoopback(address) || PublicAddressPolicy.IsPublic(address);
}

/// <summary>DNS that returns a different answer on each call, to simulate rebinding.</summary>
internal sealed class SequenceDnsResolver(params string[][] answers) : IDnsResolver
{
    private int _call;

    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        var answer = answers[Math.Min(_call++, answers.Length - 1)];
        return Task.FromResult<IReadOnlyList<IPAddress>>(answer.Select(IPAddress.Parse).ToArray());
    }
}

/// <summary>A minimal HTTP/1.1 server on 127.0.0.1 that answers every request with a fixed response.</summary>
internal sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public LoopbackHttpServer(string statusLine = "200 OK")
    {
        _listener.Start();
        _loop = Task.Run(() => AcceptLoopAsync(statusLine));
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int ConnectionCount { get; private set; }

    public List<string> Requests { get; } = [];

    private async Task AcceptLoopAsync(string statusLine)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                ConnectionCount++;
                var stream = client.GetStream();
                var buffer = new byte[8192];
                var received = new StringBuilder();
                while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer, _stop.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    received.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                Requests.Add(received.ToString());
                var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {statusLine}\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
                await stream.WriteAsync(response, _stop.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
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

internal static class Probes
{
    public static MonitoringOptions Options(int maxRedirects = 3, int maxResponseBytes = 64 * 1024, int[]? allowedPorts = null) => new()
    {
        MaxRedirects = maxRedirects,
        MaxResponseBytes = maxResponseBytes,
        AllowedPorts = allowedPorts ?? [80, 443, 8080, 8443],
    };

    public static HttpProbeRunner Runner(
        HttpMessageHandler handler, MonitoringOptions? options = null, IDnsResolver? dns = null, IAddressPolicy? policy = null)
    {
        options ??= Options();
        dns ??= new FakeDnsResolver()
            .Answer("api.example.com", "93.184.216.34")
            .Answer("other.example.com", "93.184.216.35")
            .Answer("private.example.com", "10.0.0.7");
        var validator = new DestinationValidator(dns, options.AllowedPorts, policy);
        return new HttpProbeRunner(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            validator,
            Microsoft.Extensions.Options.Options.Create(options),
            TimeProvider.System,
            NullLogger<HttpProbeRunner>.Instance);
    }

    public static ProbeRequest Request(string url, int timeoutMs = 5000, int min = 200, int max = 299) =>
        new(new Uri(url), timeoutMs, min, max);
}

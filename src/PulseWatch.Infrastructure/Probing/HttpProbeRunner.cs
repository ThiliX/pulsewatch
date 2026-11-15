using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseWatch.Application.Options;
using PulseWatch.Application.Probing;
using PulseWatch.Application.Security;
using PulseWatch.Domain.Monitoring;

namespace PulseWatch.Infrastructure.Probing;

/// <summary>
/// Runs one GET probe. Rules:
/// <list type="bullet">
/// <item>Exactly one attempt per check: no retries, so transient failures stay visible and latency is honest.</item>
/// <item>One overall timeout covers connecting, redirects, headers and the (capped) body.</item>
/// <item>No cookies, credentials or custom headers are ever sent.</item>
/// <item>Redirects are followed manually (at most MaxRedirects) and every target is fully re-validated.</item>
/// <item>HTTP/1.1 at most, so every connection goes through the SafeConnect address check.</item>
/// <item>At most MaxResponseBytes of the body are read, then the rest is discarded.</item>
/// <item>Failures become categorised results with fixed, safe summaries — never exception messages.</item>
/// </list>
/// </summary>
internal sealed partial class HttpProbeRunner(
    HttpClient client,
    IDestinationValidator destinations,
    IOptions<MonitoringOptions> options,
    TimeProvider time,
    ILogger<HttpProbeRunner> logger) : IProbeRunner
{
    private static readonly string UserAgent =
        "PulseWatch/" + (typeof(HttpProbeRunner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "0.0.0");

    public async Task<ProbeResult> ProbeAsync(ProbeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startedAt = time.GetUtcNow();
        var startTimestamp = time.GetTimestamp();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(request.TimeoutMs));

        CheckOutcome outcome;
        int? status = null;
        try
        {
            (outcome, status) = await SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the worker is shutting down; this is not a result
        }
        catch (OperationCanceledException)
        {
            outcome = CheckOutcome.Timeout;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException
            or DestinationBlockedException or DnsResolutionFailedException or AuthenticationException
            or InvalidOperationException or NotSupportedException)
        {
            outcome = Classify(ex);
            if (outcome == CheckOutcome.UnexpectedError)
            {
                LogUnexpected(logger, ex.GetType().Name);
            }
        }

        var elapsed = time.GetElapsedTime(startTimestamp);
        var duration = (int)Math.Min(int.MaxValue, Math.Round(elapsed.TotalMilliseconds));
        return new ProbeResult(
            startedAt,
            startedAt + elapsed,
            duration,
            outcome,
            status,
            Summarize(outcome, status, request.TimeoutMs));
    }

    private async Task<(CheckOutcome Outcome, int? Status)> SendAsync(ProbeRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var uri = request.Url;
        var visited = new HashSet<string>(StringComparer.Ordinal) { uri.AbsoluteUri };

        for (var redirects = 0; ; redirects++)
        {
            // HTTP/1.1 or lower only. HTTP/3 runs over QUIC, which does not go through SafeConnect's
            // ConnectCallback, so an upgrade (for example via Alt-Svc) would skip the address check.
            using var message = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            };
            message.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            message.Headers.TryAddWithoutValidation("Accept", "*/*");

            using var response = await client
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var status = (int)response.StatusCode;

            // An expected status wins, even a 3xx the owner chose to accept.
            if (status >= request.ExpectedStatusMin && status <= request.ExpectedStatusMax)
            {
                await ReadCappedBodyAsync(response, settings.MaxResponseBytes, cancellationToken).ConfigureAwait(false);
                return (CheckOutcome.Success, status);
            }

            if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
            {
                var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (redirects >= settings.MaxRedirects || !visited.Add(next.AbsoluteUri))
                {
                    return (CheckOutcome.RedirectRejected, status);
                }

                var check = await destinations.ValidateAsync(next.AbsoluteUri, cancellationToken).ConfigureAwait(false);
                if (!check.IsAllowed)
                {
                    return (CheckOutcome.RedirectRejected, status);
                }

                uri = next;
                continue;
            }

            await ReadCappedBodyAsync(response, settings.MaxResponseBytes, cancellationToken).ConfigureAwait(false);
            return (CheckOutcome.HttpStatusFailure, status);
        }
    }

    /// <summary>Reads (and discards) at most <paramref name="maxBytes"/> so a huge or endless body can't tie up the worker.</summary>
    private static async Task ReadCappedBodyAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var buffer = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                var total = 0;
                while (total < maxBytes)
                {
                    var read = await stream
                        .ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maxBytes - total)), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    internal static CheckOutcome Classify(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case DestinationBlockedException:
                    return CheckOutcome.DestinationBlocked;
                case DnsResolutionFailedException:
                    return CheckOutcome.DnsFailure;
                case AuthenticationException:
                    return CheckOutcome.TlsFailure;
                case SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData }:
                    return CheckOutcome.DnsFailure;
            }
        }

        return exception switch
        {
            HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => CheckOutcome.DnsFailure,
            HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => CheckOutcome.TlsFailure,
            HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded or HttpRequestError.ProxyTunnelError }
                => CheckOutcome.ConnectionFailure,
            IOException or SocketException => CheckOutcome.ConnectionFailure,
            _ => CheckOutcome.UnexpectedError,
        };
    }

    /// <summary>Fixed, safe text for the UI and alerts. Never includes exception messages or response content.</summary>
    internal static string? Summarize(CheckOutcome outcome, int? status, int timeoutMs) => outcome switch
    {
        CheckOutcome.Success => null,
        CheckOutcome.HttpStatusFailure => string.Create(CultureInfo.InvariantCulture, $"Unexpected HTTP status {status}."),
        CheckOutcome.Timeout => string.Create(CultureInfo.InvariantCulture, $"No complete response within {timeoutMs} ms."),
        CheckOutcome.DnsFailure => "The host name could not be resolved.",
        CheckOutcome.TlsFailure => "The TLS connection failed (for example an invalid or expired certificate).",
        CheckOutcome.ConnectionFailure => "The connection was refused, reset or closed early.",
        CheckOutcome.RedirectRejected => "A redirect was not followed (blocked destination, loop or too many redirects).",
        CheckOutcome.DestinationBlocked => "The destination resolves to a blocked address.",
        CheckOutcome.ResponseTooLarge => "The response was too large.",
        _ => "The request failed unexpectedly.",
    };

    [LoggerMessage(EventId = 3000, Level = LogLevel.Warning, Message = "Probe failed with an unclassified {ExceptionType}")]
    private static partial void LogUnexpected(ILogger logger, string exceptionType);
}

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using PulseWatch.Application.Probing;
using PulseWatch.Domain.Monitoring;

namespace PulseWatch.UnitTests.Probing;

public class HttpProbeRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Expected_status_is_a_success_and_the_request_carries_nothing_sensitive()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(FakeHandler.Status(200)));

        var result = await Probes.Runner(handler).ProbeAsync(Probes.Request("https://api.example.com/health?key=1"), Ct);

        Assert.Equal(CheckOutcome.Success, result.Outcome);
        Assert.Equal(200, result.HttpStatusCode);
        Assert.Null(result.ErrorSummary);
        Assert.True(result.CompletedAtUtc >= result.StartedAtUtc);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.StartsWith("PulseWatch/", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Null(request.Headers.Authorization);
        Assert.Null(request.Content);
    }

    [Fact]
    public async Task Unexpected_status_is_an_http_status_failure_with_the_code()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(FakeHandler.Status(503)));

        var result = await Probes.Runner(handler).ProbeAsync(Probes.Request("https://api.example.com/"), Ct);

        Assert.Equal(CheckOutcome.HttpStatusFailure, result.Outcome);
        Assert.Equal(503, result.HttpStatusCode);
        Assert.Equal("Unexpected HTTP status 503.", result.ErrorSummary);
    }

    [Fact]
    public async Task A_redirect_inside_the_expected_range_counts_as_success_and_is_not_followed()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(FakeHandler.Status(301, "https://other.example.com/")));

        var result = await Probes.Runner(handler).ProbeAsync(Probes.Request("https://api.example.com/", min: 200, max: 399), Ct);

        Assert.Equal(CheckOutcome.Success, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Relative_redirects_are_followed_to_a_validated_destination()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/old" ? FakeHandler.Status(302, "/new") : FakeHandler.Status(200)));

        var result = await Probes.Runner(handler).ProbeAsync(Probes.Request("https://api.example.com/old"), Ct);

        Assert.Equal(CheckOutcome.Success, result.Outcome);
        Assert.Equal(["https://api.example.com/old", "https://api.example.com/new"], handler.Requests.Select(r => r.RequestUri!.AbsoluteUri));
    }

    [Theory]
    [InlineData("http://127.0.0.1/admin")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://localhost:8080/")]
    [InlineData("https://private.example.com/")]   // DNS answer is 10.0.0.7
    [InlineData("ftp://other.example.com/")]
    [InlineData("https://other.example.com:6379/")] // port not allowed
    public async Task Redirects_to_blocked_destinations_are_not_followed(string location)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(FakeHandler.Status(302, location)));

        var result = await Probes.Runner(handler).ProbeAsync(Probes.Request("https://api.example.com/"), Ct);

        Assert.Equal(CheckOutcome.RedirectRejected, result.Outcome);
        Assert.Single(handler.Requests); // the blocked target was never requested
    }

    [Fact]
    public async Task Redirect_loops_are_rejected()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(
            request.RequestUri!.Host == "api.example.com"
                ? FakeHandler.Status(302, "https://other.example.com/")
                : FakeHandler.Status(302, "https://api.example.com/")));

        var result = await Probes.Runner(handler).ProbeAsync(Probes.Request("https://api.example.com/"), Ct);

        Assert.Equal(CheckOutcome.RedirectRejected, result.Outcome);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task More_redirects_than_allowed_are_rejected()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(
            FakeHandler.Status(302, $"https://api.example.com/{request.RequestUri!.AbsolutePath.Length}")));

        var result = await Probes.Runner(handler, Probes.Options(maxRedirects: 2)).ProbeAsync(Probes.Request("https://api.example.com/"), Ct);

        Assert.Equal(CheckOutcome.RedirectRejected, result.Outcome);
        Assert.Equal(3, handler.Requests.Count); // original + 2 allowed redirects
    }

    [Fact]
    public async Task No_response_headers_within_the_timeout_is_a_timeout()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return FakeHandler.Status(200);
        });
        var stopwatch = Stopwatch.StartNew();

        var result = await Probes.Runner(handler).ProbeAsync(Probes.Request("https://api.example.com/", timeoutMs: 1000), Ct);

        Assert.Equal(CheckOutcome.Timeout, result.Outcome);
        Assert.Null(result.HttpStatusCode);
        Assert.InRange(result.DurationMs, 900, 5000);
        Assert.Equal("No complete response within 1000 ms.", result.ErrorSummary);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_body_that_stalls_after_the_headers_is_a_timeout()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream()),
        }));

        var result = await Probes.Runner(handler).ProbeAsync(Probes.Request("https://api.example.com/", timeoutMs: 1000), Ct);

        Assert.Equal(CheckOutcome.Timeout, result.Outcome);
    }

    [Fact]
    public async Task Only_a_capped_number_of_body_bytes_are_read()
    {
        var body = new EndlessStream();
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(body),
        }));

        var result = await Probes.Runner(handler, Probes.Options(maxResponseBytes: 4096)).ProbeAsync(Probes.Request("https://api.example.com/"), Ct);

        Assert.Equal(CheckOutcome.Success, result.Outcome);
        Assert.InRange(body.BytesRead, 1, 4096 + 8192); // the cap plus at most one buffered read
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_turned_into_a_result()
    {
        using var cts = new CancellationTokenSource();
        var handler = new FakeHandler(async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            return FakeHandler.Status(200);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Probes.Runner(handler).ProbeAsync(Probes.Request("https://api.example.com/"), cts.Token));
    }

    public static TheoryData<string, CheckOutcome> Failures => new()
    {
        { "blocked", CheckOutcome.DestinationBlocked },
        { "dns", CheckOutcome.DnsFailure },
        { "dns-error", CheckOutcome.DnsFailure },
        { "tls", CheckOutcome.TlsFailure },
        { "refused", CheckOutcome.ConnectionFailure },
        { "reset", CheckOutcome.ConnectionFailure },
        { "io", CheckOutcome.ConnectionFailure },
        { "invalid-response", CheckOutcome.UnexpectedError },
    };

    private static Exception Make(string kind) => kind switch
    {
        "blocked" => new HttpRequestException(HttpRequestError.ConnectionError, "x", new DestinationBlockedException()),
        "dns" => new HttpRequestException(HttpRequestError.ConnectionError, "x", new DnsResolutionFailedException()),
        "dns-error" => new HttpRequestException(HttpRequestError.NameResolutionError, "x"),
        "tls" => new HttpRequestException(HttpRequestError.SecureConnectionError, "x", new AuthenticationException("bad cert")),
        "refused" => new HttpRequestException(HttpRequestError.ConnectionError, "x", new SocketException((int)SocketError.ConnectionRefused)),
        "reset" => new HttpRequestException(HttpRequestError.ResponseEnded, "x"),
        "io" => new IOException("connection reset"),
        _ => new HttpRequestException(HttpRequestError.InvalidResponse, "x"),
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Transport_failures_become_categorised_results(string kind, CheckOutcome expected)
    {
        var handler = new FakeHandler((_, _) => Task.FromException<HttpResponseMessage>(Make(kind)));

        var result = await Probes.Runner(handler).ProbeAsync(Probes.Request("https://api.example.com/"), Ct);

        Assert.Equal(expected, result.Outcome);
        Assert.NotNull(result.ErrorSummary);
        Assert.Null(result.HttpStatusCode);
    }

    [Fact]
    public async Task Exception_messages_never_reach_the_stored_summary()
    {
        var handler = new FakeHandler((_, _) => Task.FromException<HttpResponseMessage>(
            new HttpRequestException(HttpRequestError.ConnectionError, "token=secret-123 failed")));

        var result = await Probes.Runner(handler).ProbeAsync(Probes.Request("https://api.example.com/"), Ct);

        Assert.DoesNotContain("secret", result.ErrorSummary, StringComparison.Ordinal);
    }
}

using System.Net;
using System.Net.Sockets;
using PulseWatch.Application.Probing;
using PulseWatch.Application.Security;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Infrastructure.Probing;
using PulseWatch.UnitTests.Security;

namespace PulseWatch.UnitTests.Probing;

/// <summary>
/// SSRF layer 3 with real sockets on 127.0.0.1. Loopback is only reachable when a test explicitly uses
/// <see cref="LoopbackAllowedPolicy"/>; with the production policy every attempt must be refused
/// before a connection is made.
/// </summary>
public class SafeConnectTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Connects_to_the_address_it_validated()
    {
        await using var server = new LoopbackHttpServer();
        var dns = new FakeDnsResolver().Answer("target.test", "127.0.0.1");

        await using var stream = await SafeConnect.ConnectAsync(dns, new LoopbackAllowedPolicy(), new DnsEndPoint("target.test", server.Port), Ct);

        Assert.True(stream.CanWrite);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("93.184.216.34", "127.0.0.1")] // one public, one internal answer
    [InlineData("::ffff:127.0.0.1")]
    public async Task Production_policy_refuses_before_connecting(params string[] answers)
    {
        await using var server = new LoopbackHttpServer();
        var dns = new FakeDnsResolver().Answer("evil.test", answers);

        await Assert.ThrowsAsync<DestinationBlockedException>(async () =>
            await SafeConnect.ConnectAsync(dns, PublicOnlyAddressPolicy.Instance, new DnsEndPoint("evil.test", server.Port), Ct));

        await Task.Delay(100, Ct);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    public async Task Ip_literal_hosts_are_checked_too()
    {
        var dns = new FakeDnsResolver();

        await Assert.ThrowsAsync<DestinationBlockedException>(async () =>
            await SafeConnect.ConnectAsync(dns, PublicOnlyAddressPolicy.Instance, new DnsEndPoint("127.0.0.1", 80), Ct));
        Assert.Empty(dns.Queries);
    }

    [Theory]
    [InlineData("unknown.test")] // resolver throws (NXDOMAIN)
    [InlineData("empty.test")]   // resolver returns nothing
    public async Task Unresolvable_hosts_fail_as_dns_errors(string host)
    {
        var dns = new FakeDnsResolver().Answer("empty.test");

        await Assert.ThrowsAsync<DnsResolutionFailedException>(async () =>
            await SafeConnect.ConnectAsync(dns, PublicOnlyAddressPolicy.Instance, new DnsEndPoint(host, 80), Ct));
    }

    [Fact]
    public async Task Probe_through_the_real_handler_succeeds_against_an_allowed_target()
    {
        await using var server = new LoopbackHttpServer();
        var dns = new FakeDnsResolver().Answer("target.test", "127.0.0.1");
        var policy = new LoopbackAllowedPolicy();
        using var handler = SafeConnect.CreateHandler(dns, policy);
        var runner = Probes.Runner(handler, Probes.Options(allowedPorts: [server.Port]), dns, policy);

        var result = await runner.ProbeAsync(Probes.Request($"http://target.test:{server.Port}/health"), Ct);

        Assert.Equal(CheckOutcome.Success, result.Outcome);
        Assert.Equal(200, result.HttpStatusCode);
        var request = Assert.Single(server.Requests);
        Assert.StartsWith("GET /health HTTP/1.1", request, StringComparison.Ordinal);
        Assert.Contains("Host: target.test", request, StringComparison.Ordinal); // host name kept for TLS/virtual hosts
        Assert.DoesNotContain("Cookie:", request, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization:", request, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dns_rebinding_after_validation_is_blocked_at_connect_time()
    {
        await using var server = new LoopbackHttpServer();

        // First answer (when the monitor was saved) is public; later answers point at loopback.
        var dns = new SequenceDnsResolver(["93.184.216.34"], ["127.0.0.1"]);
        var validator = new DestinationValidator(dns, [server.Port]);
        var saved = await validator.ValidateAsync($"http://rebind.test:{server.Port}/", Ct);
        Assert.True(saved.IsAllowed);

        using var handler = SafeConnect.CreateHandler(dns, PublicOnlyAddressPolicy.Instance);
        var runner = Probes.Runner(handler, Probes.Options(allowedPorts: [server.Port]), dns);

        var result = await runner.ProbeAsync(Probes.Request($"http://rebind.test:{server.Port}/"), Ct);

        Assert.Equal(CheckOutcome.DestinationBlocked, result.Outcome);
        await Task.Delay(100, Ct);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    public async Task A_closed_port_is_a_connection_failure()
    {
        // Reserve a port, then close it so nothing listens there.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var dns = new FakeDnsResolver().Answer("closed.test", "127.0.0.1");
        var policy = new LoopbackAllowedPolicy();
        using var handler = SafeConnect.CreateHandler(dns, policy);
        var runner = Probes.Runner(handler, Probes.Options(allowedPorts: [port]), dns, policy);

        var result = await runner.ProbeAsync(Probes.Request($"http://closed.test:{port}/"), Ct);

        Assert.Equal(CheckOutcome.ConnectionFailure, result.Outcome);
    }
}

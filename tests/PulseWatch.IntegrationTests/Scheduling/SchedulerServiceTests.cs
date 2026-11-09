using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseWatch.Application.Probing;
using PulseWatch.Domain.Monitoring;
using PulseWatch.IntegrationTests.Infrastructure;
using PulseWatch.Worker;

namespace PulseWatch.IntegrationTests.Scheduling;

/// <summary>Runs the real worker host against PostgreSQL with a scripted probe (no network).</summary>
public class SchedulerServiceTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class ScriptedProbe : IProbeRunner
    {
        public ConcurrentDictionary<string, int> Calls { get; } = new(StringComparer.Ordinal);

        public Task<ProbeResult> ProbeAsync(ProbeRequest request, CancellationToken cancellationToken)
        {
            var url = request.Url.AbsoluteUri;
            Calls.AddOrUpdate(url, 1, (_, n) => n + 1);
            if (url.Contains("explodes", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Bug inside PulseWatch while probing.");
            }

            var now = DateTimeOffset.UtcNow;
            var outcome = url.Contains("down", StringComparison.Ordinal) ? CheckOutcome.HttpStatusFailure : CheckOutcome.Success;
            return Task.FromResult(new ProbeResult(now, now.AddMilliseconds(15), 15, outcome,
                outcome == CheckOutcome.Success ? 200 : 500, outcome == CheckOutcome.Success ? null : "Unexpected HTTP status 500."));
        }
    }

    private static IHost BuildWorker(string connectionString, ScriptedProbe probe)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PulseWatch"] = connectionString,
            ["Monitoring:PollIntervalSeconds"] = "1",
            ["Worker:HeartbeatSeconds"] = "5",
            ["Worker:WorkerId"] = "test-worker",
        });
        builder.Services.AddPulseWatchWorker(builder.Configuration);
        builder.Services.RemoveAll<IProbeRunner>();
        builder.Services.AddSingleton<IProbeRunner>(probe);
        return builder.Build();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(200, Ct);
        }
    }

    [Fact]
    public async Task Worker_checks_due_monitors_once_skips_paused_and_future_ones_and_survives_a_crashing_probe()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        Guid healthy, down, explodes, paused, future;
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var user = await TestData.AddUserAsync(db);
            EndpointMonitor Add(string url, DateTimeOffset createdAt, int failureThreshold = 3)
            {
                var m = EndpointMonitor.Create(user.Id, url, url, MonitorSettings.Default with { FailureThreshold = failureThreshold }, createdAt);
                db.Monitors.Add(m);
                return m;
            }

            healthy = Add("https://healthy.example.com/", now.AddMinutes(-1)).Id;
            down = Add("https://down.example.com/", now.AddMinutes(-1), failureThreshold: 1).Id;
            explodes = Add("https://explodes.example.com/", now.AddMinutes(-1)).Id;
            var pausedMonitor = Add("https://paused.example.com/", now.AddMinutes(-1));
            pausedMonitor.Pause(now);
            paused = pausedMonitor.Id;
            future = Add("https://future.example.com/", now.AddHours(1)).Id;
            await db.SaveChangesAsync(Ct);
        }

        var probe = new ScriptedProbe();
        using var worker = BuildWorker(connectionString, probe);
        await worker.StartAsync(Ct);

        await WaitUntilAsync(async () =>
        {
            await using var db = PostgresFixture.CreateContext(connectionString);
            return await db.MonitorChecks.CountAsync(c => c.MonitorId == healthy || c.MonitorId == down, Ct) == 2;
        }, TimeSpan.FromSeconds(15));

        // Let a few more polls run: nothing is due again for 60 s, so there must be no duplicates.
        await Task.Delay(TimeSpan.FromSeconds(2.5), Ct);
        await worker.StopAsync(Ct);

        await using var read = PostgresFixture.CreateContext(connectionString);
        var checksPerMonitor = await read.MonitorChecks.GroupBy(c => c.MonitorId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, Ct);

        Assert.Equal(1, checksPerMonitor[healthy]);
        Assert.Equal(1, checksPerMonitor[down]);
        Assert.False(checksPerMonitor.ContainsKey(explodes));
        Assert.False(checksPerMonitor.ContainsKey(paused));
        Assert.False(checksPerMonitor.ContainsKey(future));

        // The crashing monitor was attempted once and then left leased (retried after the lease expires, not in a hot loop).
        Assert.Equal(1, probe.Calls["https://explodes.example.com/"]);
        Assert.False(probe.Calls.ContainsKey("https://paused.example.com/"));
        var exploded = await read.Monitors.SingleAsync(m => m.Id == explodes, Ct);
        Assert.NotNull(exploded.LeaseUntilUtc);

        Assert.Equal(MonitorState.Healthy, (await read.Monitors.SingleAsync(m => m.Id == healthy, Ct)).State);
        Assert.Equal(MonitorState.Down, (await read.Monitors.SingleAsync(m => m.Id == down, Ct)).State);
        Assert.Equal(1, await read.Incidents.CountAsync(i => i.MonitorId == down, Ct));

        var heartbeat = await read.WorkerHeartbeats.SingleAsync(Ct);
        Assert.Equal("test-worker", heartbeat.WorkerId);
    }

    [Fact]
    public async Task Worker_keeps_running_when_the_database_is_unreachable()
    {
        var probe = new ScriptedProbe();
        using var worker = BuildWorker("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1", probe);

        await worker.StartAsync(Ct);
        await Task.Delay(TimeSpan.FromSeconds(2.5), Ct);
        await worker.StopAsync(Ct);

        Assert.Empty(probe.Calls);
    }
}

using Microsoft.EntityFrameworkCore;
using PulseWatch.Application.Probing;
using PulseWatch.Application.Scheduling;
using PulseWatch.Domain.Incidents;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Infrastructure.Persistence;
using PulseWatch.Infrastructure.Scheduling;
using PulseWatch.IntegrationTests.Infrastructure;

namespace PulseWatch.IntegrationTests.Scheduling;

public class ClaimAndRecordTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(45);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Real "now", rounded to milliseconds so PostgreSQL's microsecond precision round-trips exactly.</summary>
    private static DateTimeOffset Now()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Ticks - (now.Ticks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);
    }

    private static async Task<EndpointMonitor> AddMonitorAsync(
        PulseWatchDbContext db, string ownerId, DateTimeOffset createdAt, MonitorSettings? settings = null, string url = "https://api.example.com/")
    {
        var monitor = EndpointMonitor.Create(ownerId, "API", url, settings ?? MonitorSettings.Default, createdAt);
        db.Monitors.Add(monitor);
        await db.SaveChangesAsync(Ct);
        return monitor;
    }

    private static ClaimedMonitor Claimed(EndpointMonitor m) =>
        new(m.Id, m.Url, m.TimeoutMs, m.ExpectedStatusMin, m.ExpectedStatusMax);

    private static ProbeResult Result(CheckOutcome outcome, DateTimeOffset startedAt, int? status = null) =>
        new(startedAt, startedAt.AddMilliseconds(120), 120, outcome,
            status ?? (outcome == CheckOutcome.Success ? 200 : outcome == CheckOutcome.HttpStatusFailure ? 503 : null),
            outcome == CheckOutcome.Success ? null : "failed");

    [Fact]
    public async Task Claims_only_enabled_due_unleased_monitors_oldest_first_up_to_the_batch_size()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var user = await TestData.AddUserAsync(db);
        var now = TestData.Now;
        var oldest = await AddMonitorAsync(db, user.Id, now.AddMinutes(-3));
        var older = await AddMonitorAsync(db, user.Id, now.AddMinutes(-2));
        var due = await AddMonitorAsync(db, user.Id, now.AddMinutes(-1));
        var notDue = await AddMonitorAsync(db, user.Id, now.AddMinutes(5));
        var paused = await AddMonitorAsync(db, user.Id, now.AddMinutes(-10));
        paused.Pause(now);
        await db.SaveChangesAsync(Ct);

        var first = await new MonitorClaimer(db).ClaimDueAsync(now, batchSize: 2, Lease, Ct);
        var second = await new MonitorClaimer(db).ClaimDueAsync(now, batchSize: 10, Lease, Ct);
        var third = await new MonitorClaimer(db).ClaimDueAsync(now, batchSize: 10, Lease, Ct);

        Assert.Equal([oldest.Id, older.Id], first.Select(c => c.Id));
        Assert.Equal([due.Id], second.Select(c => c.Id));
        Assert.Empty(third); // the rest is leased, paused or not due
        Assert.DoesNotContain(first.Concat(second), c => c.Id == notDue.Id || c.Id == paused.Id);
        Assert.Equal("https://api.example.com/", first[0].Url);
    }

    [Fact]
    public async Task An_expired_lease_is_claimed_again()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var user = await TestData.AddUserAsync(db);
        var monitor = await AddMonitorAsync(db, user.Id, TestData.Now);

        var claimer = new MonitorClaimer(db);
        Assert.Single(await claimer.ClaimDueAsync(TestData.Now, 10, Lease, Ct));
        Assert.Empty(await claimer.ClaimDueAsync(TestData.Now + Lease - TimeSpan.FromSeconds(1), 10, Lease, Ct));

        // The worker that held it "crashed": once the lease runs out another worker takes over.
        var reclaimed = await claimer.ClaimDueAsync(TestData.Now + Lease + TimeSpan.FromSeconds(1), 10, Lease, Ct);
        Assert.Equal(monitor.Id, Assert.Single(reclaimed).Id);
    }

    [Fact]
    public async Task Concurrent_workers_never_claim_the_same_monitor()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var setup = PostgresFixture.CreateContext(connectionString))
        {
            var user = await TestData.AddUserAsync(setup);
            for (var i = 0; i < 60; i++)
            {
                setup.Monitors.Add(EndpointMonitor.Create(user.Id, $"M{i}", $"https://m{i}.example.com/", MonitorSettings.Default, TestData.Now));
            }

            await setup.SaveChangesAsync(Ct);
        }

        // Eight "workers" claim in parallel, each with its own connection, until nothing is left.
        var workers = Enumerable.Range(0, 8).Select(async _ =>
        {
            var mine = new List<Guid>();
            await using var db = PostgresFixture.CreateContext(connectionString);
            var claimer = new MonitorClaimer(db);
            while (true)
            {
                var batch = await claimer.ClaimDueAsync(TestData.Now, 5, Lease, Ct);
                if (batch.Count == 0)
                {
                    return mine;
                }

                mine.AddRange(batch.Select(c => c.Id));
            }
        });

        var all = (await Task.WhenAll(workers)).SelectMany(ids => ids).ToList();

        Assert.Equal(60, all.Count);
        Assert.Equal(60, all.Distinct().Count());
    }

    [Fact]
    public async Task Recording_a_success_saves_the_check_updates_health_and_schedules_the_next_check()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var user = await TestData.AddUserAsync(db);
        var now = Now();
        var monitor = await AddMonitorAsync(db, user.Id, now.AddMinutes(-1));
        var claimed = Assert.Single(await new MonitorClaimer(db).ClaimDueAsync(now, 10, Lease, Ct));

        // Each step uses its own DbContext, as the worker's scopes do.
        await using var recorderDb = PostgresFixture.CreateContext(connectionString);
        var outcome = await new CheckResultRecorder(recorderDb, TimeProvider.System).RecordAsync(claimed, Result(CheckOutcome.Success, now), Ct);

        Assert.Equal(RecordOutcome.Recorded, outcome);
        await using var read = PostgresFixture.CreateContext(connectionString);
        var saved = await read.Monitors.SingleAsync(m => m.Id == monitor.Id, Ct);
        var check = await read.MonitorChecks.SingleAsync(c => c.MonitorId == monitor.Id, Ct);
        Assert.Equal(MonitorState.Healthy, saved.State);
        Assert.Equal(CheckOutcome.Success, saved.LastOutcome);
        Assert.Equal(now.AddSeconds(60), saved.NextCheckAtUtc);
        Assert.Null(saved.LeaseUntilUtc);
        Assert.Equal(200, check.HttpStatusCode);
        Assert.Equal(120, check.DurationMs);
    }

    [Fact]
    public async Task Repeated_failures_open_exactly_one_incident_at_the_threshold()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        Guid monitorId;
        await using (var setup = PostgresFixture.CreateContext(connectionString))
        {
            var user = await TestData.AddUserAsync(setup);
            monitorId = (await AddMonitorAsync(setup, user.Id, Now().AddMinutes(-10))).Id;
        }

        for (var i = 0; i < 5; i++)
        {
            await using var db = PostgresFixture.CreateContext(connectionString);
            var monitor = await db.Monitors.AsNoTracking().SingleAsync(m => m.Id == monitorId, Ct);
            await new CheckResultRecorder(db, TimeProvider.System)
                .RecordAsync(Claimed(monitor), Result(CheckOutcome.Timeout, Now().AddSeconds(i)), Ct);
        }

        await using var read = PostgresFixture.CreateContext(connectionString);
        var incident = await read.Incidents.SingleAsync(i => i.MonitorId == monitorId, Ct);
        Assert.Equal(IncidentStatus.Open, incident.Status);
        Assert.Equal(5, incident.FailureCount);
        Assert.Equal(CheckOutcome.Timeout, incident.OpeningOutcome);
        Assert.Equal(5, await read.MonitorChecks.CountAsync(c => c.MonitorId == monitorId, Ct));
        Assert.Equal(MonitorState.Down, (await read.Monitors.SingleAsync(m => m.Id == monitorId, Ct)).State);
    }

    [Fact]
    public async Task A_result_for_a_monitor_deleted_during_the_check_is_dropped()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var user = await TestData.AddUserAsync(db);
        var monitor = await AddMonitorAsync(db, user.Id, Now());
        var claimed = Claimed(monitor);
        await db.Monitors.Where(m => m.Id == monitor.Id).ExecuteDeleteAsync(Ct);

        await using var recorderDb = PostgresFixture.CreateContext(connectionString);
        var outcome = await new CheckResultRecorder(recorderDb, TimeProvider.System).RecordAsync(claimed, Result(CheckOutcome.Success, Now()), Ct);

        Assert.Equal(RecordOutcome.MonitorDeleted, outcome);
        Assert.Equal(0, await recorderDb.MonitorChecks.CountAsync(Ct));
    }

    [Fact]
    public async Task A_result_for_a_url_changed_during_the_check_is_dropped_and_the_lease_released()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var user = await TestData.AddUserAsync(db);
        var now = Now();
        var monitor = await AddMonitorAsync(db, user.Id, now.AddMinutes(-1));
        var claimed = Assert.Single(await new MonitorClaimer(db).ClaimDueAsync(now, 10, Lease, Ct));
        await using (var api = PostgresFixture.CreateContext(connectionString))
        {
            var edited = await api.Monitors.SingleAsync(m => m.Id == monitor.Id, Ct);
            edited.UpdateDetails("API", "https://new.example.com/", edited.Settings, false, now);
            await api.SaveChangesAsync(Ct);
        }

        await using var recorderDb = PostgresFixture.CreateContext(connectionString);
        var outcome = await new CheckResultRecorder(recorderDb, TimeProvider.System).RecordAsync(claimed, Result(CheckOutcome.HttpStatusFailure, now), Ct);

        Assert.Equal(RecordOutcome.UrlChanged, outcome);
        Assert.Equal(0, await recorderDb.MonitorChecks.CountAsync(Ct));
        var saved = await recorderDb.Monitors.SingleAsync(m => m.Id == monitor.Id, Ct);
        Assert.Null(saved.LeaseUntilUtc);
        Assert.Equal(MonitorState.Unknown, saved.State);
    }

    [Fact]
    public async Task A_monitor_paused_during_the_check_keeps_the_result_but_opens_no_incident()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var user = await TestData.AddUserAsync(db);
        var now = Now();
        var monitor = await AddMonitorAsync(db, user.Id, now.AddMinutes(-1), MonitorSettings.Default with { FailureThreshold = 1 });
        var claimed = Assert.Single(await new MonitorClaimer(db).ClaimDueAsync(now, 10, Lease, Ct));
        await using (var api = PostgresFixture.CreateContext(connectionString))
        {
            var edited = await api.Monitors.SingleAsync(m => m.Id == monitor.Id, Ct);
            edited.Pause(now);
            await api.SaveChangesAsync(Ct);
        }

        await using var recorderDb = PostgresFixture.CreateContext(connectionString);
        await new CheckResultRecorder(recorderDb, TimeProvider.System).RecordAsync(claimed, Result(CheckOutcome.ConnectionFailure, now), Ct);

        Assert.Equal(1, await recorderDb.MonitorChecks.CountAsync(Ct));
        Assert.Equal(0, await recorderDb.Incidents.CountAsync(Ct));
        Assert.Equal(MonitorState.Paused, (await recorderDb.Monitors.SingleAsync(m => m.Id == monitor.Id, Ct)).State);
    }

    [Fact]
    public async Task Heartbeat_is_inserted_then_updated()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var store = new WorkerHeartbeatStore(db);

        await store.BeatAsync("worker-a", "1.0.0", TestData.Now, Ct);
        await store.BeatAsync("worker-a", "1.0.1", TestData.Now.AddSeconds(30), Ct);
        await store.BeatAsync("worker-a", "1.0.1", TestData.Now.AddSeconds(10), Ct); // late, out-of-order beat

        var heartbeat = await db.WorkerHeartbeats.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(TestData.Now, heartbeat.StartedAtUtc);
        Assert.Equal(TestData.Now.AddSeconds(30), heartbeat.LastSeenAtUtc);
        Assert.Equal("1.0.1", heartbeat.Version);
    }
}

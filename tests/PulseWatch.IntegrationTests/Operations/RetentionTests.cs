using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseWatch.Domain.Monitoring;
using PulseWatch.IntegrationTests.Infrastructure;
using PulseWatch.Worker;

namespace PulseWatch.IntegrationTests.Operations;

public class RetentionTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IHost BuildWorker(string connectionString, int batchSize) =>
        BuildWorker(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PulseWatch"] = connectionString,
            ["Monitoring:CheckRetentionDays"] = "30",
            ["Retention:BatchSize"] = batchSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Retention:BatchPauseMs"] = "0",
        });

    private static IHost BuildWorker(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddPulseWatchWorker(builder.Configuration);
        return builder.Build();
    }

    private static RetentionService Retention(IHost host) =>
        host.Services.GetServices<IHostedService>().OfType<RetentionService>().Single();

    [Fact]
    public async Task Deletes_only_checks_older_than_the_retention_period_in_batches_and_keeps_incidents()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        Guid incidentId;
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            // An incident from 60 days ago, with its email: older than retention, but must survive.
            var (_, oldIncidentId, _) = await NotificationTestData.AddPendingMessageAsync(db, now.AddDays(-60));
            incidentId = oldIncidentId;
            var monitorId = (await db.Incidents.SingleAsync(i => i.Id == incidentId, Ct)).MonitorId;

            // 250 checks older than 30 days (3 batches of 100), and 40 recent ones (one is 29 days old).
            for (var i = 0; i < 250; i++)
            {
                var at = now.AddDays(-31).AddMinutes(-i);
                db.MonitorChecks.Add(MonitorCheck.Create(monitorId, at, at.AddMilliseconds(10), 10, CheckOutcome.Success, 200, null));
            }

            for (var i = 0; i < 40; i++)
            {
                var at = now.AddDays(-29).AddMinutes(i);
                db.MonitorChecks.Add(MonitorCheck.Create(monitorId, at, at.AddMilliseconds(10), 10, CheckOutcome.Success, 200, null));
            }

            await db.SaveChangesAsync(Ct);
        }

        using var worker = BuildWorker(connectionString, batchSize: 100);
        var deleted = await Retention(worker).RunOnceAsync(Ct);

        await using var read = PostgresFixture.CreateContext(connectionString);
        // The 250 old checks plus the incident's own failed check from 60 days ago.
        Assert.Equal(251, deleted);
        Assert.Equal(40, await read.MonitorChecks.CountAsync(Ct));
        Assert.False(await read.MonitorChecks.AnyAsync(c => c.StartedAtUtc < now.AddDays(-30), Ct));
        Assert.True(await read.Incidents.AnyAsync(i => i.Id == incidentId, Ct));
        Assert.Equal(1, await read.NotificationOutbox.CountAsync(m => m.IncidentId == incidentId, Ct));

        // Nothing left to do on the next run.
        Assert.Equal(0, await Retention(worker).RunOnceAsync(Ct));
    }

    [Fact]
    public async Task An_empty_database_is_fine()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        using var worker = BuildWorker(connectionString, batchSize: 5000);

        Assert.Equal(0, await Retention(worker).RunOnceAsync(Ct));
    }

    [Fact]
    public async Task The_retention_query_uses_the_started_at_index()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        await db.Database.OpenConnectionAsync(Ct); // keep one connection so the SET applies to the EXPLAIN
        await db.Database.ExecuteSqlRawAsync("SET enable_seqscan = off", Ct); // tiny table: make the planner show its index choice
        var plan = string.Join('\n', await db.Database
            .SqlQueryRaw<string>("""
                EXPLAIN SELECT id FROM monitor_checks
                WHERE started_at_utc < now() - interval '30 days'
                ORDER BY started_at_utc LIMIT 5000
                """)
            .ToListAsync(Ct));

        Assert.Contains("ix_monitor_checks_started_at", plan, StringComparison.Ordinal);
    }
}

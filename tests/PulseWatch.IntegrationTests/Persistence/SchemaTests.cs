using Microsoft.EntityFrameworkCore;
using Npgsql;
using PulseWatch.Domain.Incidents;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Notifications;
using PulseWatch.IntegrationTests.Infrastructure;

namespace PulseWatch.IntegrationTests.Persistence;

public class SchemaTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrations_apply_to_an_empty_database_and_leave_nothing_pending()
    {
        var connectionString = await postgres.CreateDatabaseAsync(migrate: false);
        await using var db = PostgresFixture.CreateContext(connectionString);

        await db.Database.MigrateAsync(Ct);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
        var tables = await db.Database
            .SqlQuery<string>($"SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync(Ct);
        Assert.Superset(
            new HashSet<string> { "users", "monitors", "monitor_checks", "incidents", "notification_outbox", "worker_heartbeats" },
            tables.ToHashSet());
    }

    [Fact]
    public async Task Monitor_and_check_round_trip_with_utc_times_and_enums()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        Guid monitorId;
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var user = await TestData.AddUserAsync(db);
            var monitor = await TestData.AddMonitorAsync(db, user.Id);
            var check = MonitorCheck.Create(
                monitor.Id, TestData.Now, TestData.Now.AddMilliseconds(250), 250, CheckOutcome.Timeout, null, "Request timed out");
            monitor.ApplyCheckResult(check, null);
            db.MonitorChecks.Add(check);
            await db.SaveChangesAsync(Ct);
            monitorId = monitor.Id;
        }

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var monitor = await db.Monitors.SingleAsync(m => m.Id == monitorId, Ct);
            var check = await db.MonitorChecks.SingleAsync(c => c.MonitorId == monitorId, Ct);

            Assert.Equal(MonitorState.Degraded, monitor.State);
            Assert.Equal(CheckOutcome.Timeout, monitor.LastOutcome);
            Assert.Equal(TimeSpan.Zero, monitor.CreatedAtUtc.Offset);
            Assert.Equal(TestData.Now, monitor.StreakStartedAtUtc);
            Assert.NotEqual(0u, monitor.Version);
            Assert.Equal(CheckOutcome.Timeout, check.Outcome);
            Assert.Equal("Request timed out", check.ErrorSummary);
            Assert.Equal(TestData.Now.AddMilliseconds(250), check.CompletedAtUtc);
        }
    }

    [Fact]
    public async Task Database_rejects_a_second_open_incident_for_the_same_monitor()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var user = await TestData.AddUserAsync(db);
        var monitor = await TestData.AddMonitorAsync(db, user.Id, MonitorSettings.Default with { FailureThreshold = 1 });

        var first = monitor.ApplyCheckResult(TestData.Failure(monitor, TestData.Now), null).Incident!;
        db.Incidents.Add(first);
        await db.SaveChangesAsync(Ct);

        // Simulate a bug or race where the caller forgets the open incident: the domain opens another one.
        var second = monitor.ApplyCheckResult(TestData.Failure(monitor, TestData.Now.AddMinutes(1)), null).Incident!;
        db.Incidents.Add(second);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
        Assert.Equal("ux_incidents_one_open_per_monitor", pg.ConstraintName);
    }

    [Fact]
    public async Task Resolved_incidents_do_not_block_a_new_open_incident()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var user = await TestData.AddUserAsync(db);
        var monitor = await TestData.AddMonitorAsync(db, user.Id, MonitorSettings.Default with { FailureThreshold = 1 });

        var first = monitor.ApplyCheckResult(TestData.Failure(monitor, TestData.Now), null).Incident!;
        db.Incidents.Add(first);
        await db.SaveChangesAsync(Ct);
        monitor.ApplyCheckResult(TestData.Success(monitor, TestData.Now.AddMinutes(1)), first);
        await db.SaveChangesAsync(Ct);
        var second = monitor.ApplyCheckResult(TestData.Failure(monitor, TestData.Now.AddMinutes(2)), null).Incident!;
        db.Incidents.Add(second);
        await db.SaveChangesAsync(Ct);

        var statuses = await db.Incidents.Where(i => i.MonitorId == monitor.Id).Select(i => i.Status).ToListAsync(Ct);
        Assert.Equal(2, statuses.Count);
        Assert.Contains(IncidentStatus.Open, statuses);
        Assert.Contains(IncidentStatus.Resolved, statuses);
    }

    [Theory]
    [InlineData("UPDATE monitors SET interval_seconds = 10", "ck_monitors_interval")]
    [InlineData("UPDATE monitors SET timeout_ms = 60000", "ck_monitors_timeout")]
    [InlineData("UPDATE monitors SET failure_threshold = 0", "ck_monitors_failure_threshold")]
    [InlineData("UPDATE monitors SET expected_status_min = 400, expected_status_max = 200", "ck_monitors_expected_status")]
    [InlineData("UPDATE monitors SET consecutive_failures = -1", "ck_monitors_counters")]
    public async Task Check_constraints_reject_values_that_bypass_the_domain(string sql, string constraint)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var user = await TestData.AddUserAsync(db);
        await TestData.AddMonitorAsync(db, user.Id);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, Ct));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal(constraint, ex.ConstraintName);
    }

    [Fact]
    public async Task Duplicate_notification_idempotency_key_is_rejected()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var user = await TestData.AddUserAsync(db);
        var monitor = await TestData.AddMonitorAsync(db, user.Id, MonitorSettings.Default with { FailureThreshold = 1 });
        var incident = monitor.ApplyCheckResult(TestData.Failure(monitor, TestData.Now), null).Incident!;
        db.Incidents.Add(incident);
        db.NotificationOutbox.Add(NotificationOutboxMessage.Create(
            incident.Id, NotificationEventType.IncidentOpened, NotificationChannel.Email, user.Email!, TestData.Now));
        await db.SaveChangesAsync(Ct);

        db.NotificationOutbox.Add(NotificationOutboxMessage.Create(
            incident.Id, NotificationEventType.IncidentOpened, NotificationChannel.Email, user.Email!.ToUpperInvariant(), TestData.Now));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [Fact]
    public async Task Deleting_a_monitor_removes_its_checks_incidents_and_notifications()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var user = await TestData.AddUserAsync(db);
        var monitor = await TestData.AddMonitorAsync(db, user.Id, MonitorSettings.Default with { FailureThreshold = 1 });
        var check = TestData.Failure(monitor, TestData.Now);
        var incident = monitor.ApplyCheckResult(check, null).Incident!;
        db.MonitorChecks.Add(check);
        db.Incidents.Add(incident);
        db.NotificationOutbox.Add(NotificationOutboxMessage.Create(
            incident.Id, NotificationEventType.IncidentOpened, NotificationChannel.Email, user.Email!, TestData.Now));
        await db.SaveChangesAsync(Ct);

        await db.Monitors.Where(m => m.Id == monitor.Id).ExecuteDeleteAsync(Ct);

        Assert.Equal(0, await db.MonitorChecks.CountAsync(Ct));
        Assert.Equal(0, await db.Incidents.CountAsync(Ct));
        Assert.Equal(0, await db.NotificationOutbox.CountAsync(Ct));
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_monitors_but_not_other_users_monitors()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var alice = await TestData.AddUserAsync(db);
        var bob = await TestData.AddUserAsync(db);
        await TestData.AddMonitorAsync(db, alice.Id);
        var bobsMonitor = await TestData.AddMonitorAsync(db, bob.Id);

        await db.Users.Where(u => u.Id == alice.Id).ExecuteDeleteAsync(Ct);

        var remaining = await db.Monitors.Select(m => m.Id).ToListAsync(Ct);
        Assert.Equal([bobsMonitor.Id], remaining);
    }

    [Fact]
    public async Task Concurrent_edits_to_the_same_monitor_are_detected()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        Guid monitorId;
        await using (var setup = PostgresFixture.CreateContext(connectionString))
        {
            var user = await TestData.AddUserAsync(setup);
            monitorId = (await TestData.AddMonitorAsync(setup, user.Id)).Id;
        }

        await using var first = PostgresFixture.CreateContext(connectionString);
        await using var second = PostgresFixture.CreateContext(connectionString);
        var copyA = await first.Monitors.SingleAsync(m => m.Id == monitorId, Ct);
        var copyB = await second.Monitors.SingleAsync(m => m.Id == monitorId, Ct);

        copyA.Pause(TestData.Now.AddMinutes(1));
        await first.SaveChangesAsync(Ct);
        copyB.Pause(TestData.Now.AddMinutes(2));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));
    }
}

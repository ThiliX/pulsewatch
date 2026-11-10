using Microsoft.EntityFrameworkCore;
using PulseWatch.Application.Probing;
using PulseWatch.Application.Scheduling;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Notifications;
using PulseWatch.Infrastructure.Notifications;
using PulseWatch.Infrastructure.Scheduling;
using PulseWatch.IntegrationTests.Infrastructure;

namespace PulseWatch.IntegrationTests.Notifications;

/// <summary>Outbox rows are written by CheckResultRecorder in the same transaction as the incident change.</summary>
public class OutboxRecordingTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProbeResult Result(CheckOutcome outcome, DateTimeOffset at) =>
        new(at, at.AddMilliseconds(50), 50, outcome, outcome == CheckOutcome.Success ? 200 : 503,
            outcome == CheckOutcome.Success ? null : "failed");

    private static async Task RecordAsync(string connectionString, EndpointMonitor monitor, CheckOutcome outcome, DateTimeOffset at)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        var recorder = new CheckResultRecorder(db, new ManualTimeProvider(at.AddSeconds(1)));
        var claimed = new ClaimedMonitor(monitor.Id, monitor.Url, monitor.TimeoutMs, monitor.ExpectedStatusMin, monitor.ExpectedStatusMax);
        Assert.Equal(RecordOutcome.Recorded, (await recorder.RecordAsync(claimed, Result(outcome, at), Ct)).Outcome);
    }

    private static async Task<List<NotificationOutboxMessage>> MessagesAsync(string connectionString)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        return await db.NotificationOutbox.AsNoTracking().OrderBy(m => m.CreatedAtUtc).ToListAsync(Ct);
    }

    [Fact]
    public async Task Opening_and_resolving_each_queue_exactly_one_email_to_the_owner()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        EndpointMonitor monitor;
        string ownerEmail;
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var owner = await TestData.AddUserAsync(db, "Owner.Person@Example.com");
            ownerEmail = owner.Email!;
            monitor = await TestData.AddMonitorAsync(db, owner.Id,
                MonitorSettings.Default with { FailureThreshold = 2, RecoveryThreshold = 2 });
        }

        var t = TestData.Now;

        // One failure: below the threshold, no incident, no email.
        await RecordAsync(connectionString, monitor, CheckOutcome.HttpStatusFailure, t.AddMinutes(1));
        Assert.Empty(await MessagesAsync(connectionString));

        // Second failure opens the incident: exactly one "opened" email.
        await RecordAsync(connectionString, monitor, CheckOutcome.HttpStatusFailure, t.AddMinutes(2));
        var opened = Assert.Single(await MessagesAsync(connectionString));
        Assert.Equal(NotificationEventType.IncidentOpened, opened.EventType);
        Assert.Equal(NotificationChannel.Email, opened.Channel);
        Assert.Equal(ownerEmail, opened.Recipient);
        Assert.Equal(NotificationStatus.Pending, opened.Status);
        Assert.Equal(0, opened.AttemptCount);

        // More failures and the first success (recovery threshold 2) queue nothing.
        await RecordAsync(connectionString, monitor, CheckOutcome.Timeout, t.AddMinutes(3));
        await RecordAsync(connectionString, monitor, CheckOutcome.Success, t.AddMinutes(4));
        Assert.Single(await MessagesAsync(connectionString));

        // The second success resolves: exactly one "resolved" email for the same incident.
        await RecordAsync(connectionString, monitor, CheckOutcome.Success, t.AddMinutes(5));
        await RecordAsync(connectionString, monitor, CheckOutcome.Success, t.AddMinutes(6));
        var messages = await MessagesAsync(connectionString);
        Assert.Equal(2, messages.Count);
        Assert.Equal(NotificationEventType.IncidentResolved, messages[1].EventType);
        Assert.Equal(opened.IncidentId, messages[1].IncidentId);

        await using var read = PostgresFixture.CreateContext(connectionString);
        Assert.Equal(6, await read.MonitorChecks.CountAsync(Ct));
    }

    [Fact]
    public async Task A_duplicate_idempotency_key_is_ignored_without_failing_the_transaction()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var (_, incidentId, recipient) = await NotificationTestData.AddPendingMessageAsync(db, TestData.Now);

        // Same incident, event, channel and recipient (different case): same key, so not inserted again.
        var duplicate = NotificationOutboxMessage.Create(
            incidentId, NotificationEventType.IncidentOpened, NotificationChannel.Email, recipient.ToUpperInvariant(), TestData.Now);
        var resolved = NotificationOutboxMessage.Create(
            incidentId, NotificationEventType.IncidentResolved, NotificationChannel.Email, recipient, TestData.Now);

        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        Assert.Equal(0, await OutboxWriter.EnqueueAsync(db, duplicate, Ct));
        Assert.Equal(1, await OutboxWriter.EnqueueAsync(db, resolved, Ct));
        await transaction.CommitAsync(Ct);

        Assert.Equal(2, await db.NotificationOutbox.CountAsync(m => m.IncidentId == incidentId, Ct));
    }

    [Fact]
    public async Task Deleting_the_monitor_removes_its_queued_emails()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        await NotificationTestData.AddPendingMessageAsync(db, TestData.Now);

        await db.Monitors.ExecuteDeleteAsync(Ct);

        Assert.Equal(0, await db.NotificationOutbox.CountAsync(Ct));
    }
}

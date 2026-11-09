using PulseWatch.Domain.Common;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Notifications;
using PulseWatch.Domain.Workers;

namespace PulseWatch.UnitTests.Domain;

public class MonitorCheckAndOutboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid MonitorId = Guid.CreateVersion7();

    [Fact]
    public void Check_rejects_completion_before_start() =>
        Assert.Throws<DomainException>(() =>
            MonitorCheck.Create(MonitorId, Now, Now.AddSeconds(-1), 0, CheckOutcome.Timeout, null, null));

    [Fact]
    public void Check_rejects_negative_duration() =>
        Assert.Throws<DomainException>(() =>
            MonitorCheck.Create(MonitorId, Now, Now, -1, CheckOutcome.Timeout, null, null));

    [Theory]
    [InlineData(CheckOutcome.Success)]
    [InlineData(CheckOutcome.HttpStatusFailure)]
    public void Status_based_outcomes_require_a_status_code(CheckOutcome outcome) =>
        Assert.Throws<DomainException>(() =>
            MonitorCheck.Create(MonitorId, Now, Now, 10, outcome, null, null));

    [Theory]
    [InlineData(99)]
    [InlineData(600)]
    public void Check_rejects_impossible_status_codes(int status) =>
        Assert.Throws<DomainException>(() =>
            MonitorCheck.Create(MonitorId, Now, Now, 10, CheckOutcome.HttpStatusFailure, status, null));

    [Fact]
    public void Check_truncates_long_error_summaries_and_drops_blank_ones()
    {
        var longSummary = MonitorCheck.Create(MonitorId, Now, Now, 10, CheckOutcome.Timeout, null, new string('x', 500));
        var blankSummary = MonitorCheck.Create(MonitorId, Now, Now, 10, CheckOutcome.Timeout, null, "   ");

        Assert.Equal(MonitorLimits.ErrorSummaryMaxLength, longSummary.ErrorSummary!.Length);
        Assert.Null(blankSummary.ErrorSummary);
    }

    [Fact]
    public void Outbox_message_starts_pending_and_due_now()
    {
        var incidentId = Guid.CreateVersion7();

        var message = NotificationOutboxMessage.Create(
            incidentId, NotificationEventType.IncidentOpened, NotificationChannel.Email, " owner@example.com ", Now);

        Assert.Equal(NotificationStatus.Pending, message.Status);
        Assert.Equal(Now, message.NextAttemptAtUtc);
        Assert.Equal(0, message.AttemptCount);
        Assert.Equal("owner@example.com", message.Recipient);
    }

    [Fact]
    public void Idempotency_key_is_the_same_for_the_same_event_and_recipient_regardless_of_case()
    {
        var incidentId = Guid.CreateVersion7();

        var a = NotificationOutboxMessage.Create(incidentId, NotificationEventType.IncidentOpened, NotificationChannel.Email, "Owner@Example.com", Now);
        var b = NotificationOutboxMessage.Create(incidentId, NotificationEventType.IncidentOpened, NotificationChannel.Email, "owner@example.com", Now.AddMinutes(1));
        var resolved = NotificationOutboxMessage.Create(incidentId, NotificationEventType.IncidentResolved, NotificationChannel.Email, "owner@example.com", Now);

        Assert.Equal(a.IdempotencyKey, b.IdempotencyKey);
        Assert.NotEqual(a.IdempotencyKey, resolved.IdempotencyKey);
        Assert.True(a.IdempotencyKey.Length <= NotificationOutboxMessage.IdempotencyKeyMaxLength);
    }

    [Theory]
    [InlineData("owner@example.com\r\nBcc: victim@example.com")]
    [InlineData("owner@example.com\nX: y")]
    [InlineData("")]
    public void Outbox_rejects_header_injection_and_empty_recipients(string recipient) =>
        Assert.Throws<DomainException>(() => NotificationOutboxMessage.Create(
            Guid.CreateVersion7(), NotificationEventType.IncidentOpened, NotificationChannel.Email, recipient, Now));

    [Fact]
    public void Heartbeat_only_moves_forward()
    {
        var heartbeat = WorkerHeartbeat.Create("worker-1", "1.0.0", Now);

        heartbeat.Beat(Now.AddSeconds(30));
        heartbeat.Beat(Now.AddSeconds(10));

        Assert.Equal(Now.AddSeconds(30), heartbeat.LastSeenAtUtc);
        Assert.Equal(Now, heartbeat.StartedAtUtc);
    }
}

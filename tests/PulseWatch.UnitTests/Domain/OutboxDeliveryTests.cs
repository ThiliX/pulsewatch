using PulseWatch.Domain.Common;
using PulseWatch.Domain.Notifications;

namespace PulseWatch.UnitTests.Domain;

public class OutboxDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(3);

    private static NotificationOutboxMessage NewMessage() =>
        NotificationOutboxMessage.Create(
            Guid.CreateVersion7(), NotificationEventType.IncidentOpened, NotificationChannel.Email, "owner@example.com", Now);

    [Fact]
    public void Begin_attempt_counts_the_attempt_and_takes_a_lease()
    {
        var message = NewMessage();

        message.BeginAttempt(Now, Now + Lease);

        Assert.Equal(NotificationStatus.Sending, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal(Now, message.LastAttemptAtUtc);
        Assert.Equal(Now + Lease, message.LeaseUntilUtc);
    }

    [Fact]
    public void Success_marks_sent_and_clears_the_lease_and_error()
    {
        var message = NewMessage();
        message.BeginAttempt(Now, Now + Lease);
        message.MarkFailed(Now, "ConnectionFailed", permanent: false, maxAttempts: 6, TimeSpan.FromSeconds(30));
        message.BeginAttempt(Now.AddSeconds(30), Now.AddSeconds(30) + Lease);

        message.MarkSent(Now.AddSeconds(31));

        Assert.Equal(NotificationStatus.Sent, message.Status);
        Assert.Equal(2, message.AttemptCount);
        Assert.Equal(Now.AddSeconds(31), message.SentAtUtc);
        Assert.Null(message.LeaseUntilUtc);
        Assert.Null(message.LastErrorCategory);
        Assert.False(message.IsDue(Now.AddDays(1)));
    }

    [Fact]
    public void Transient_failure_schedules_a_retry_and_is_not_due_before_it()
    {
        var message = NewMessage();
        message.BeginAttempt(Now, Now + Lease);

        message.MarkFailed(Now, "Timeout", permanent: false, maxAttempts: 6, TimeSpan.FromSeconds(30));

        Assert.Equal(NotificationStatus.Failed, message.Status);
        Assert.Equal("Timeout", message.LastErrorCategory);
        Assert.Equal(Now.AddSeconds(30), message.NextAttemptAtUtc);
        Assert.False(message.IsDue(Now.AddSeconds(29)));
        Assert.True(message.IsDue(Now.AddSeconds(30)));
    }

    [Fact]
    public void Running_out_of_attempts_marks_exhausted()
    {
        var message = NewMessage();
        var at = Now;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            message.BeginAttempt(at, at + Lease);
            message.MarkFailed(at, "ConnectionFailed", permanent: false, maxAttempts: 3, TimeSpan.FromSeconds(1));
            at = at.AddSeconds(1);
        }

        Assert.Equal(NotificationStatus.Exhausted, message.Status);
        Assert.Equal(3, message.AttemptCount);
        Assert.False(message.IsDue(Now.AddDays(1)));
        Assert.Throws<DomainException>(() => message.BeginAttempt(Now.AddDays(1), Now.AddDays(1) + Lease));
    }

    [Fact]
    public void Permanent_failure_marks_exhausted_on_the_first_attempt()
    {
        var message = NewMessage();
        message.BeginAttempt(Now, Now + Lease);

        message.MarkFailed(Now, "RecipientRejected", permanent: true, maxAttempts: 6, TimeSpan.FromSeconds(30));

        Assert.Equal(NotificationStatus.Exhausted, message.Status);
        Assert.Equal(1, message.AttemptCount);
    }

    [Fact]
    public void A_sending_message_is_due_again_only_after_its_lease_expires()
    {
        var message = NewMessage();
        message.BeginAttempt(Now, Now + Lease);

        Assert.False(message.IsDue(Now + Lease));
        Assert.True(message.IsDue(Now + Lease + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Results_can_only_be_recorded_while_sending()
    {
        var message = NewMessage();

        Assert.Throws<DomainException>(() => message.MarkSent(Now));
        Assert.Throws<DomainException>(() =>
            message.MarkFailed(Now, "Timeout", permanent: false, maxAttempts: 6, TimeSpan.Zero));
    }

    [Fact]
    public void Begin_attempt_rejects_a_message_that_is_not_due_yet_and_a_lease_in_the_past()
    {
        var message = NewMessage();

        Assert.Throws<DomainException>(() => message.BeginAttempt(Now.AddSeconds(-1), Now + Lease));
        Assert.Throws<DomainException>(() => message.BeginAttempt(Now, Now));
    }
}

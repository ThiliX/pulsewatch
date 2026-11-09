using PulseWatch.Domain.Common;

namespace PulseWatch.Domain.Notifications;

/// <summary>Stored as smallint; do not renumber.</summary>
public enum NotificationEventType : short
{
    IncidentOpened = 0,
    IncidentResolved = 1,
}

/// <summary>Stored as smallint; do not renumber.</summary>
public enum NotificationChannel : short
{
    Email = 0,
}

/// <summary>Stored as smallint; do not renumber.</summary>
public enum NotificationStatus : short
{
    Pending = 0,
    Sending = 1,
    Sent = 2,
    Failed = 3,
    Exhausted = 4,
}

/// <summary>
/// A notification waiting to be delivered. It is written in the same transaction as the incident
/// change that caused it, so a committed incident event cannot be silently lost.
/// <para>Delivery lifecycle: the dispatcher claims due rows with one SQL statement that sets
/// <see cref="NotificationStatus.Sending"/>, counts the attempt and takes a lease. Counting at claim
/// time means a message that crashes the worker mid-send still runs out of attempts. After the send,
/// <see cref="MarkSent"/> or <see cref="MarkFailed"/> records the result. A row whose lease expires
/// while Sending is claimed again (at-least-once delivery).</para>
/// </summary>
public sealed class NotificationOutboxMessage
{
    public const int RecipientMaxLength = 256;
    public const int IdempotencyKeyMaxLength = 300;
    public const int ErrorCategoryMaxLength = 100;

    private NotificationOutboxMessage()
    {
        Recipient = string.Empty;
        IdempotencyKey = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid IncidentId { get; private set; }
    public NotificationEventType EventType { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public string Recipient { get; private set; }
    public string IdempotencyKey { get; private set; }
    public NotificationStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset NextAttemptAtUtc { get; private set; }
    public DateTimeOffset? LeaseUntilUtc { get; private set; }
    public DateTimeOffset? LastAttemptAtUtc { get; private set; }
    public DateTimeOffset? SentAtUtc { get; private set; }
    public string? LastErrorCategory { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static NotificationOutboxMessage Create(
        Guid incidentId,
        NotificationEventType eventType,
        NotificationChannel channel,
        string recipient,
        DateTimeOffset nowUtc)
    {
        Guard.Utc(nowUtc);
        var cleanRecipient = Guard.Text(recipient, RecipientMaxLength);
        if (cleanRecipient.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new DomainException("Recipient must not contain line breaks.");
        }

        return new NotificationOutboxMessage
        {
            Id = Guid.CreateVersion7(nowUtc),
            IncidentId = incidentId,
            EventType = eventType,
            Channel = channel,
            Recipient = cleanRecipient,
            IdempotencyKey = BuildIdempotencyKey(incidentId, eventType, channel, cleanRecipient),
            Status = NotificationStatus.Pending,
            NextAttemptAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
        };
    }

    /// <summary>One key per incident event, channel and recipient. A unique index on it prevents duplicate sends.</summary>
    public static string BuildIdempotencyKey(
        Guid incidentId, NotificationEventType eventType, NotificationChannel channel, string recipient) =>
        $"{incidentId:N}:{eventType}:{channel}:{recipient.Trim().ToUpperInvariant()}";

    /// <summary>Whether the dispatcher may claim this message at <paramref name="nowUtc"/>.</summary>
    public bool IsDue(DateTimeOffset nowUtc) => Status switch
    {
        NotificationStatus.Pending or NotificationStatus.Failed => NextAttemptAtUtc <= nowUtc,
        NotificationStatus.Sending => LeaseUntilUtc < nowUtc, // the previous sender died mid-send
        _ => false,
    };

    /// <summary>
    /// Starts a delivery attempt. The dispatcher's claim SQL applies exactly this change to many rows at
    /// once (see OutboxStore in Infrastructure); keep the two in sync.
    /// </summary>
    public void BeginAttempt(DateTimeOffset nowUtc, DateTimeOffset leaseUntilUtc)
    {
        Guard.Utc(nowUtc);
        Guard.Utc(leaseUntilUtc);
        if (!IsDue(nowUtc))
        {
            throw new DomainException("The message is not due for delivery.");
        }

        if (leaseUntilUtc <= nowUtc)
        {
            throw new DomainException("The lease must end in the future.");
        }

        Status = NotificationStatus.Sending;
        AttemptCount++;
        LastAttemptAtUtc = nowUtc;
        LeaseUntilUtc = leaseUntilUtc;
    }

    /// <summary>The send succeeded.</summary>
    public void MarkSent(DateTimeOffset nowUtc)
    {
        Guard.Utc(nowUtc);
        EnsureSending();
        Status = NotificationStatus.Sent;
        SentAtUtc = nowUtc;
        LastAttemptAtUtc = nowUtc;
        LeaseUntilUtc = null;
        LastErrorCategory = null;
    }

    /// <summary>
    /// The send failed. A permanent error, or running out of attempts, ends in
    /// <see cref="NotificationStatus.Exhausted"/>; otherwise the message is retried after <paramref name="retryDelay"/>.
    /// </summary>
    /// <param name="errorCategory">A short fixed label such as "ConnectionFailed". Never an exception message.</param>
    public void MarkFailed(DateTimeOffset nowUtc, string errorCategory, bool permanent, int maxAttempts, TimeSpan retryDelay)
    {
        Guard.Utc(nowUtc);
        EnsureSending();
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero);

        LastAttemptAtUtc = nowUtc;
        LeaseUntilUtc = null;
        LastErrorCategory = Guard.Text(errorCategory, ErrorCategoryMaxLength);
        if (permanent || AttemptCount >= maxAttempts)
        {
            Status = NotificationStatus.Exhausted;
            return;
        }

        Status = NotificationStatus.Failed;
        NextAttemptAtUtc = nowUtc + retryDelay;
    }

    private void EnsureSending()
    {
        if (Status != NotificationStatus.Sending)
        {
            throw new DomainException("Only a message that is being sent can record a delivery result.");
        }
    }
}

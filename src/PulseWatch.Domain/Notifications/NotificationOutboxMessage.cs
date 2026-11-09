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
/// Delivery and retry behaviour is added in Milestone 5.
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
}

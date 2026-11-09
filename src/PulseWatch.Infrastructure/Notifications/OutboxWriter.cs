using Microsoft.EntityFrameworkCore;
using PulseWatch.Domain.Notifications;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Infrastructure.Notifications;

/// <summary>Adds outbox rows inside the caller's transaction.</summary>
internal static class OutboxWriter
{
    /// <summary>
    /// Inserts the message unless one with the same idempotency key already exists. A duplicate is
    /// ignored instead of failing, so it can never roll back the check and incident change it belongs to.
    /// </summary>
    /// <returns>1 if inserted, 0 if it was a duplicate.</returns>
    public static Task<int> EnqueueAsync(PulseWatchDbContext db, NotificationOutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(message);

        return db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO notification_outbox
                (id, incident_id, event_type, channel, recipient, idempotency_key, status, attempt_count, next_attempt_at_utc, created_at_utc)
            VALUES
                ({message.Id}, {message.IncidentId}, {(short)message.EventType}, {(short)message.Channel}, {message.Recipient},
                 {message.IdempotencyKey}, {(short)message.Status}, {message.AttemptCount}, {message.NextAttemptAtUtc}, {message.CreatedAtUtc})
            ON CONFLICT (idempotency_key) DO NOTHING
            """,
            cancellationToken);
    }
}

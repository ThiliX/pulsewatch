using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PulseWatch.Application.Notifications;
using PulseWatch.Domain.Notifications;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Infrastructure.Notifications;

/// <summary>
/// Outbox persistence for the dispatcher. Claiming uses the same SKIP LOCKED + lease pattern as monitor
/// claiming, so several workers never send the same row at the same time.
/// </summary>
internal sealed class OutboxStore(PulseWatchDbContext db) : INotificationOutboxStore
{
    private const short Pending = (short)NotificationStatus.Pending;
    private const short Sending = (short)NotificationStatus.Sending;
    private const short Failed = (short)NotificationStatus.Failed;
    private const short Exhausted = (short)NotificationStatus.Exhausted;

    // A sender that died mid-send on its last allowed attempt: don't send again, mark it Exhausted.
    private static readonly string ExhaustInterruptedSql = $"""
        UPDATE notification_outbox
        SET status = {Exhausted}, lease_until_utc = NULL, last_error_category = @category
        WHERE status = {Sending} AND lease_until_utc < @now AND attempt_count >= @max_attempts
        """;

    // Same change as NotificationOutboxMessage.BeginAttempt, applied to a batch in one statement.
    private static readonly string ClaimSql = $"""
        UPDATE notification_outbox AS o
        SET status = {Sending},
            attempt_count = o.attempt_count + 1,
            last_attempt_at_utc = @now,
            lease_until_utc = @lease_until
        WHERE o.id IN (
            SELECT id FROM notification_outbox
            WHERE (status IN ({Pending}, {Failed}) AND next_attempt_at_utc <= @now)
               OR (status = {Sending} AND lease_until_utc < @now)
            ORDER BY next_attempt_at_utc
            LIMIT @batch_size
            FOR UPDATE SKIP LOCKED)
        RETURNING o.id, o.attempt_count
        """;

    public async Task<IReadOnlyList<ClaimedNotification>> ClaimDueAsync(
        DateTimeOffset nowUtc, int batchSize, TimeSpan lease, int maxAttempts, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using (var exhaust = connection.CreateCommand())
            {
                exhaust.CommandText = ExhaustInterruptedSql;
                exhaust.Parameters.Add(new NpgsqlParameter("now", nowUtc));
                exhaust.Parameters.Add(new NpgsqlParameter("max_attempts", maxAttempts));
                exhaust.Parameters.Add(new NpgsqlParameter("category", EmailErrorCategories.Interrupted));
                await exhaust.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = ClaimSql;
            command.Parameters.Add(new NpgsqlParameter("now", nowUtc));
            command.Parameters.Add(new NpgsqlParameter("lease_until", nowUtc + lease));
            command.Parameters.Add(new NpgsqlParameter("batch_size", batchSize));

            var claimed = new List<ClaimedNotification>(batchSize);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                claimed.Add(new ClaimedNotification(reader.GetGuid(0), reader.GetInt32(1)));
            }

            return claimed;
        }
        finally
        {
            if (openedHere)
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    public Task<NotificationEmailData?> LoadEmailDataAsync(Guid messageId, CancellationToken cancellationToken) =>
        (from message in db.NotificationOutbox
         join incident in db.Incidents on message.IncidentId equals incident.Id
         join monitor in db.Monitors on incident.MonitorId equals monitor.Id
         where message.Id == messageId
         select new NotificationEmailData(
             message.EventType,
             message.Recipient,
             incident.Id,
             monitor.Name,
             monitor.Url,
             incident.OpeningOutcome,
             incident.OpeningStatusCode,
             incident.FirstFailureAtUtc,
             incident.OpenedAtUtc,
             incident.ResolvedAtUtc,
             incident.FailureCount))
        .AsNoTracking()
        .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> MarkSentAsync(ClaimedNotification claimed, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        CompleteAsync(claimed, message => message.MarkSent(nowUtc), cancellationToken);

    public Task<bool> MarkFailedAsync(
        ClaimedNotification claimed,
        DateTimeOffset nowUtc,
        string errorCategory,
        bool permanent,
        int maxAttempts,
        TimeSpan retryDelay,
        CancellationToken cancellationToken) =>
        CompleteAsync(
            claimed,
            message => message.MarkFailed(nowUtc, errorCategory, permanent, maxAttempts, retryDelay),
            cancellationToken);

    private async Task<bool> CompleteAsync(
        ClaimedNotification claimed, Action<NotificationOutboxMessage> apply, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.Database
            .ExecuteSqlAsync($"SELECT 1 FROM notification_outbox WHERE id = {claimed.Id} FOR UPDATE", cancellationToken)
            .ConfigureAwait(false);
        var message = await db.NotificationOutbox
            .SingleOrDefaultAsync(m => m.Id == claimed.Id, cancellationToken)
            .ConfigureAwait(false);

        // Gone (incident or monitor deleted), or our lease expired and another attempt has started.
        if (message is null || message.Status != NotificationStatus.Sending || message.AttemptCount != claimed.AttemptCount)
        {
            return false;
        }

        apply(message);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}

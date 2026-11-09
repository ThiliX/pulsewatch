namespace PulseWatch.Application.Notifications;

/// <summary>
/// A message leased by this dispatcher. <see cref="AttemptCount"/> is the attempt number just started;
/// it also identifies this lease, so a slow dispatcher can't overwrite a newer attempt's result.
/// </summary>
public sealed record ClaimedNotification(Guid Id, int AttemptCount);

public interface INotificationOutboxStore
{
    /// <summary>
    /// Leases up to <paramref name="batchSize"/> due messages (Pending/Failed and due, or Sending with an
    /// expired lease) and starts an attempt on each. Sending rows already at <paramref name="maxAttempts"/>
    /// are marked Exhausted instead of being sent again.
    /// </summary>
    Task<IReadOnlyList<ClaimedNotification>> ClaimDueAsync(
        DateTimeOffset nowUtc, int batchSize, TimeSpan lease, int maxAttempts, CancellationToken cancellationToken);

    /// <summary>The data for the email, or null if the message (or its incident or monitor) was deleted.</summary>
    Task<NotificationEmailData?> LoadEmailDataAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>Records a successful send. Returns false if the row is gone or another attempt took over.</summary>
    Task<bool> MarkSentAsync(ClaimedNotification claimed, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Records a failed send. Returns false if the row is gone or another attempt took over.</summary>
    Task<bool> MarkFailedAsync(
        ClaimedNotification claimed,
        DateTimeOffset nowUtc,
        string errorCategory,
        bool permanent,
        int maxAttempts,
        TimeSpan retryDelay,
        CancellationToken cancellationToken);
}

using Microsoft.Extensions.Options;
using PulseWatch.Application.Notifications;
using PulseWatch.Application.Options;
using PulseWatch.Domain.Notifications;

namespace PulseWatch.Worker;

/// <summary>
/// Sends queued incident emails. Every poll it leases a batch of due outbox rows, sends each through
/// <see cref="IEmailSender"/> and records the result:
/// <list type="bullet">
/// <item>Success → Sent.</item>
/// <item>Transient failure → Failed, retried with exponential backoff and jitter (<see cref="RetryBackoff"/>).</item>
/// <item>Permanent failure or MaxAttempts reached → Exhausted, visible on the incident.</item>
/// </list>
/// Delivery is at-least-once: if the worker dies after the SMTP server accepted a mail but before it is
/// marked Sent, the lease expires and the mail is sent again. Email problems never affect check recording,
/// which runs in <see cref="SchedulerService"/> with its own scopes and connections.
/// </summary>
public sealed partial class OutboxDispatcherService(
    IServiceScopeFactory scopes,
    IOptions<NotificationOptions> options,
    IOptions<AppOptions> appOptions,
    TimeProvider time,
    ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    private static readonly TimeSpan LeaseMargin = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.PollIntervalSeconds), time);
        LogStarted(logger, settings.Provider, settings.PollIntervalSeconds, settings.MaxAttempts);

        try
        {
            do
            {
                await PollAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogStopping(logger);
        }
    }

    /// <summary>Sends batches until no full batch is left.</summary>
    internal async Task PollAsync(CancellationToken stoppingToken)
    {
        var batchSize = options.Value.BatchSize;
        int claimed;
        do
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                claimed = await DispatchBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Usually the database is unreachable. Keep the loop alive and try again next poll.
                LogPollFailed(logger, ex);
                return;
            }
        }
        while (claimed == batchSize);
    }

    /// <summary>Claims one batch and sends it. Returns how many messages were claimed.</summary>
    internal async Task<int> DispatchBatchAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        // The lease covers the whole batch even if every send hits its timeout.
        var rounds = (int)Math.Ceiling(settings.BatchSize / (double)settings.MaxConcurrentSends);
        var lease = (TimeSpan.FromSeconds(settings.SendTimeoutSeconds) * rounds) + LeaseMargin;

        IReadOnlyList<ClaimedNotification> batch;
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            batch = await scope.ServiceProvider.GetRequiredService<INotificationOutboxStore>()
                .ClaimDueAsync(time.GetUtcNow(), settings.BatchSize, lease, settings.MaxAttempts, stoppingToken)
                .ConfigureAwait(false);
        }

        if (batch.Count == 0)
        {
            return 0;
        }

        // CancellationToken.None on purpose: a claimed send runs to completion (bounded by its timeout)
        // and its result is recorded, so a shutdown doesn't cause a duplicate on the next start.
        await Parallel.ForEachAsync(
            batch,
            new ParallelOptions { MaxDegreeOfParallelism = settings.MaxConcurrentSends },
            async (message, _) => await DeliverAsync(message, settings).ConfigureAwait(false)).ConfigureAwait(false);

        return batch.Count;
    }

    private async Task DeliverAsync(ClaimedNotification claimed, NotificationOptions settings)
    {
        try
        {
            var scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var store = scope.ServiceProvider.GetRequiredService<INotificationOutboxStore>();
                var data = await store.LoadEmailDataAsync(claimed.Id, CancellationToken.None).ConfigureAwait(false);
                if (data is null)
                {
                    return; // deleted together with its monitor or incident
                }

                var email = NotificationEmailBuilder.Build(data, appOptions.Value.GetPublicBaseUri());
                string? category = null;
                var permanent = false;
                try
                {
                    await scope.ServiceProvider.GetRequiredService<IEmailSender>()
                        .SendAsync(email, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (EmailDeliveryException ex)
                {
                    (category, permanent) = (ex.Category, ex.Permanent);
                    LogSendFailed(logger, claimed.Id, claimed.AttemptCount, ex.Category, ex.InnerException ?? ex);
                }
                catch (Exception ex)
                {
                    category = EmailErrorCategories.Unknown;
                    LogSendFailed(logger, claimed.Id, claimed.AttemptCount, category, ex);
                }

                var now = time.GetUtcNow();
                if (category is null)
                {
                    await store.MarkSentAsync(claimed, now, CancellationToken.None).ConfigureAwait(false);
                    LogSent(logger, claimed.Id, data.EventType, data.IncidentId, claimed.AttemptCount);
                    return;
                }

                var delay = RetryBackoff.For(claimed.AttemptCount, Random.Shared.NextDouble());
                await store.MarkFailedAsync(claimed, now, category, permanent, settings.MaxAttempts, delay, CancellationToken.None)
                    .ConfigureAwait(false);
                if (permanent || claimed.AttemptCount >= settings.MaxAttempts)
                {
                    LogExhausted(logger, claimed.Id, data.IncidentId, claimed.AttemptCount, category);
                }
            }
        }
        catch (Exception ex)
        {
            // Couldn't load or record. The lease expires and the message is tried again.
            LogDeliveryFailed(logger, claimed.Id, ex);
        }
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information,
        Message = "Outbox dispatcher started: provider {Provider}, poll every {PollIntervalSeconds}s, up to {MaxAttempts} attempts")]
    private static partial void LogStarted(ILogger logger, EmailProvider provider, int pollIntervalSeconds, int maxAttempts);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Outbox dispatcher stopping")]
    private static partial void LogStopping(ILogger logger);

    // Recipients are not logged here: the message id identifies the row.
    [LoggerMessage(EventId = 3002, Level = LogLevel.Information,
        Message = "Sent notification {MessageId} ({EventType}) for incident {IncidentId} on attempt {Attempt}")]
    private static partial void LogSent(ILogger logger, Guid messageId, NotificationEventType eventType, Guid incidentId, int attempt);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning,
        Message = "Sending notification {MessageId} failed on attempt {Attempt}: {Category}")]
    private static partial void LogSendFailed(ILogger logger, Guid messageId, int attempt, string category, Exception exception);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Error,
        Message = "Notification {MessageId} for incident {IncidentId} gave up after {Attempts} attempts: {Category}")]
    private static partial void LogExhausted(ILogger logger, Guid messageId, Guid incidentId, int attempts, string category);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Error,
        Message = "Delivering notification {MessageId} failed inside PulseWatch; it will be retried after its lease expires")]
    private static partial void LogDeliveryFailed(ILogger logger, Guid messageId, Exception exception);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Error, Message = "Polling the notification outbox failed; retrying on the next poll")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);
}

using System.Diagnostics;
using Microsoft.Extensions.Options;
using PulseWatch.Application.Options;
using PulseWatch.Application.Probing;
using PulseWatch.Application.Scheduling;
using PulseWatch.Application.Telemetry;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Infrastructure.Telemetry;

namespace PulseWatch.Worker;

/// <summary>
/// The monitoring loop. Every poll it leases a batch of due monitors, probes them with bounded
/// concurrency and records each result. Guarantees:
/// <list type="bullet">
/// <item>One failing or slow monitor can't stop the others: each runs in its own scope and try/catch,
/// and each probe has a hard timeout.</item>
/// <item>A crash or failed write loses at most that one check: the lease expires and the monitor is claimed again.</item>
/// <item>On shutdown no new batch is claimed; in-flight probes finish (bounded by their timeouts) and are recorded.</item>
/// </list>
/// </summary>
public sealed partial class SchedulerService(
    IServiceScopeFactory scopes,
    IOptions<MonitoringOptions> options,
    TimeProvider time,
    PulseWatchMetrics metrics,
    ILogger<SchedulerService> logger) : BackgroundService
{
    private static readonly TimeSpan LeaseMargin = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var lease = TimeSpan.FromMilliseconds(settings.MaxTimeoutMs) + LeaseMargin;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.PollIntervalSeconds), time);
        LogStarted(logger, settings.PollIntervalSeconds, settings.BatchSize, settings.MaxConcurrentChecks);

        try
        {
            do
            {
                await PollAsync(settings, lease, stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogStopping(logger);
        }
    }

    /// <summary>Claims and processes batches until no full batch is left (drains a backlog quickly).</summary>
    internal async Task PollAsync(MonitoringOptions settings, TimeSpan lease, CancellationToken stoppingToken)
    {
        int claimed;
        do
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                claimed = await RunBatchAsync(settings, lease, stoppingToken).ConfigureAwait(false);
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
        while (claimed == settings.BatchSize);
    }

    private async Task<int> RunBatchAsync(MonitoringOptions settings, TimeSpan lease, CancellationToken stoppingToken)
    {
        IReadOnlyList<ClaimedMonitor> batch;
        var claimedAt = time.GetUtcNow();
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            batch = await scope.ServiceProvider.GetRequiredService<IMonitorClaimer>()
                .ClaimDueAsync(claimedAt, settings.BatchSize, lease, stoppingToken)
                .ConfigureAwait(false);
        }

        foreach (var monitor in batch)
        {
            if (monitor.DueAtUtc is { } due)
            {
                metrics.CheckClaimed(claimedAt - due);
            }
        }

        if (batch.Count == 0)
        {
            return 0;
        }

        LogClaimed(logger, batch.Count);

        // CancellationToken.None on purpose: once claimed, a check runs to completion (bounded by its
        // timeout) so a shutdown doesn't throw away work that is nearly done.
        await Parallel.ForEachAsync(
            batch,
            new ParallelOptions { MaxDegreeOfParallelism = settings.MaxConcurrentChecks },
            async (monitor, _) => await ProcessAsync(monitor).ConfigureAwait(false)).ConfigureAwait(false);

        return batch.Count;
    }

    private async Task ProcessAsync(ClaimedMonitor monitor)
    {
        // One span per check. Without a tracing exporter nothing listens to the ActivitySource, so a plain
        // Activity is started instead: it still gives every log line of this check the same TraceId.
        using var activity = PulseWatchMetrics.ActivitySource.StartActivity("pulsewatch.check")
            ?? new Activity("pulsewatch.check").Start();
        activity.SetTag("pulsewatch.monitor_id", monitor.Id);

        // The URL is logged as scheme + host only; paths and query strings can hold secrets.
        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["MonitorId"] = monitor.Id,
            ["Target"] = LogRedaction.UrlOrigin(monitor.Url),
        });

        try
        {
            var scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var probe = scope.ServiceProvider.GetRequiredService<IProbeRunner>();
                var result = await probe.ProbeAsync(
                        new ProbeRequest(new Uri(monitor.Url), monitor.TimeoutMs, monitor.ExpectedStatusMin, monitor.ExpectedStatusMax),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                var recorder = scope.ServiceProvider.GetRequiredService<ICheckResultRecorder>();
                var recorded = await recorder.RecordAsync(monitor, result, CancellationToken.None).ConfigureAwait(false);
                if (recorded.Outcome == RecordOutcome.Recorded)
                {
                    metrics.CheckCompleted(result.Outcome, result.DurationMs);
                }

                using (logger.BeginScope(new Dictionary<string, object?> { ["CheckId"] = recorded.CheckId }))
                {
                    LogChecked(logger, monitor.Id, result.Outcome, result.HttpStatusCode, result.DurationMs, recorded.Outcome);
                    if (recorded.IncidentChange != IncidentChange.None)
                    {
                        LogIncidentChanged(logger, monitor.Id, recorded.IncidentChange);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Isolate failures per monitor. The lease expires and the monitor is retried later.
            LogCheckFailed(logger, monitor.Id, ex);
        }
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information,
        Message = "Scheduler started: poll every {PollIntervalSeconds}s, batch {BatchSize}, up to {MaxConcurrentChecks} concurrent checks")]
    private static partial void LogStarted(ILogger logger, int pollIntervalSeconds, int batchSize, int maxConcurrentChecks);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Scheduler stopping")]
    private static partial void LogStopping(ILogger logger);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Debug, Message = "Claimed {Count} due monitors")]
    private static partial void LogClaimed(ILogger logger, int count);

    // URLs are not logged: query strings may contain secrets. The monitor id identifies the target.
    [LoggerMessage(EventId = 2003, Level = LogLevel.Information,
        Message = "Checked monitor {MonitorId}: {Outcome} {StatusCode} in {DurationMs} ms ({RecordOutcome})")]
    private static partial void LogChecked(
        ILogger logger, Guid monitorId, CheckOutcome outcome, int? statusCode, int durationMs, RecordOutcome recordOutcome);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Warning, Message = "Incident {IncidentChange} for monitor {MonitorId}")]
    private static partial void LogIncidentChanged(ILogger logger, Guid monitorId, IncidentChange incidentChange);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Error, Message = "Check of monitor {MonitorId} failed inside PulseWatch; it will be retried after its lease expires")]
    private static partial void LogCheckFailed(ILogger logger, Guid monitorId, Exception exception);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Error, Message = "Polling for due monitors failed; retrying on the next poll")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);
}

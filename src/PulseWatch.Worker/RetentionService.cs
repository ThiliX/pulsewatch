using Microsoft.Extensions.Options;
using PulseWatch.Application.Options;
using PulseWatch.Application.Scheduling;
using PulseWatch.Infrastructure.Telemetry;

namespace PulseWatch.Worker;

/// <summary>
/// Deletes checks older than <see cref="MonitoringOptions.CheckRetentionDays"/> once a day, in small batches
/// with a pause between them. Incidents and email history are kept. A failed run is logged and retried on
/// the next interval; it never stops the worker.
/// </summary>
public sealed partial class RetentionService(
    IServiceScopeFactory scopes,
    IOptions<RetentionOptions> options,
    IOptions<MonitoringOptions> monitoringOptions,
    TimeProvider time,
    PulseWatchMetrics metrics,
    ILogger<RetentionService> logger) : BackgroundService
{
    /// <summary>Upper bound per run, so a huge backlog is spread over several runs instead of one endless loop.</summary>
    internal const int MaxBatchesPerRun = 2000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        LogStarted(logger, monitoringOptions.Value.CheckRetentionDays, settings.IntervalHours);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(settings.StartDelaySeconds), time, stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(settings.IntervalHours), time);
            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    LogRunFailed(logger, ex);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>Deletes everything older than the retention period. Returns the number of checks deleted.</summary>
    internal async Task<long> RunOnceAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var cutoff = time.GetUtcNow() - TimeSpan.FromDays(monitoringOptions.Value.CheckRetentionDays);
        long total = 0;

        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            int deleted;
            var scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                deleted = await scope.ServiceProvider.GetRequiredService<ICheckRetentionStore>()
                    .DeleteChecksBeforeAsync(cutoff, settings.BatchSize, stoppingToken)
                    .ConfigureAwait(false);
            }

            total += deleted;
            metrics.ChecksDeleted(deleted);
            if (deleted < settings.BatchSize)
            {
                break;
            }

            if (settings.BatchPauseMs > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(settings.BatchPauseMs), time, stoppingToken).ConfigureAwait(false);
            }
        }

        LogRunFinished(logger, total, cutoff);
        return total;
    }

    [LoggerMessage(EventId = 4000, Level = LogLevel.Information,
        Message = "Retention started: keeping {RetentionDays} days of checks, cleanup every {IntervalHours} h")]
    private static partial void LogStarted(ILogger logger, int retentionDays, int intervalHours);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "Retention deleted {Count} checks older than {CutoffUtc:O}")]
    private static partial void LogRunFinished(ILogger logger, long count, DateTimeOffset cutoffUtc);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Error, Message = "Retention cleanup failed; retrying at the next interval")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}

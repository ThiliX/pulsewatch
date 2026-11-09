using System.Reflection;
using Microsoft.Extensions.Options;
using PulseWatch.Application.Scheduling;

namespace PulseWatch.Worker;

/// <summary>
/// Writes this worker's "last seen" time to worker_heartbeats, so the dashboard can tell
/// "your endpoint is down" apart from "the PulseWatch worker is down". A failed write is
/// logged and retried on the next beat; it never stops the worker.
/// </summary>
public sealed partial class HeartbeatService(
    IServiceScopeFactory scopes,
    ILogger<HeartbeatService> logger,
    IOptions<HeartbeatOptions> options,
    TimeProvider timeProvider) : BackgroundService
{
    private static readonly string Version =
        typeof(HeartbeatService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = options.Value.HeartbeatSeconds;
        var workerId = string.IsNullOrWhiteSpace(options.Value.WorkerId) ? Environment.MachineName : options.Value.WorkerId.Trim();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds), timeProvider);

        LogStarted(logger, workerId, seconds);
        try
        {
            do
            {
                await BeatAsync(workerId, stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogStopping(logger);
        }
    }

    private async Task BeatAsync(string workerId, CancellationToken stoppingToken)
    {
        try
        {
            var scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                await scope.ServiceProvider.GetRequiredService<IWorkerHeartbeatStore>()
                    .BeatAsync(workerId, Version, timeProvider.GetUtcNow(), stoppingToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            LogBeatFailed(logger, ex);
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Worker {WorkerId} started; heartbeat every {HeartbeatSeconds}s")]
    private static partial void LogStarted(ILogger logger, string workerId, int heartbeatSeconds);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Worker stopping")]
    private static partial void LogStopping(ILogger logger);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Could not write the worker heartbeat; will retry")]
    private static partial void LogBeatFailed(ILogger logger, Exception exception);
}

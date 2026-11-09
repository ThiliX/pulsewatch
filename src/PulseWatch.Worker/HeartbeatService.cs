using Microsoft.Extensions.Options;

namespace PulseWatch.Worker;

/// <summary>
/// Logs that the worker is alive. From Milestone 3 this also writes a row to worker_heartbeats,
/// so the dashboard can tell "your API is down" apart from "the PulseWatch worker is down".
/// </summary>
public sealed partial class HeartbeatService(
    ILogger<HeartbeatService> logger,
    IOptions<HeartbeatOptions> options,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = options.Value.HeartbeatSeconds;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds), timeProvider);

        LogStarted(logger, seconds);
        try
        {
            do
            {
                LogHeartbeat(logger);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogStopping(logger);
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Worker started; heartbeat every {HeartbeatSeconds}s")]
    private static partial void LogStarted(ILogger logger, int heartbeatSeconds);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Worker heartbeat")]
    private static partial void LogHeartbeat(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Worker stopping")]
    private static partial void LogStopping(ILogger logger);
}

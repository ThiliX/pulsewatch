using PulseWatch.Application.Probing;

namespace PulseWatch.Application.Scheduling;

/// <summary>A due monitor that this worker has leased and must check.</summary>
public sealed record ClaimedMonitor(Guid Id, string Url, int TimeoutMs, int ExpectedStatusMin, int ExpectedStatusMax);

public interface IMonitorClaimer
{
    /// <summary>
    /// Atomically leases up to <paramref name="batchSize"/> enabled monitors that are due and not leased.
    /// Safe to call from several workers at once: a monitor is never handed to two workers while its lease is valid.
    /// </summary>
    Task<IReadOnlyList<ClaimedMonitor>> ClaimDueAsync(
        DateTimeOffset nowUtc, int batchSize, TimeSpan lease, CancellationToken cancellationToken);
}

public enum RecordOutcome
{
    /// <summary>The check was saved and the monitor's health and schedule were updated.</summary>
    Recorded = 0,

    /// <summary>The monitor was deleted during the check; the result was dropped.</summary>
    MonitorDeleted = 1,

    /// <summary>The URL changed during the check, so the result described the old URL and was dropped.</summary>
    UrlChanged = 2,
}

public interface ICheckResultRecorder
{
    /// <summary>Saves one probe result and applies it to the monitor (state, incidents, next check) in one transaction.</summary>
    Task<RecordOutcome> RecordAsync(ClaimedMonitor claimed, ProbeResult result, CancellationToken cancellationToken);
}

public interface IWorkerHeartbeatStore
{
    Task BeatAsync(string workerId, string version, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

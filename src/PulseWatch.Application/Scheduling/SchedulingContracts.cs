using PulseWatch.Application.Probing;

namespace PulseWatch.Application.Scheduling;

/// <summary>A due monitor that this worker has leased and must check.</summary>
/// <param name="DueAtUtc">When the check was due; the gap to the claim time is the worker's lag.</param>
public sealed record ClaimedMonitor(
    Guid Id, string Url, int TimeoutMs, int ExpectedStatusMin, int ExpectedStatusMax, DateTimeOffset? DueAtUtc = null);

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

/// <summary>What recording did, and the stored check's id when it was saved.</summary>
public sealed record RecordResult(RecordOutcome Outcome, Guid? CheckId = null, IncidentChange IncidentChange = IncidentChange.None);

public enum IncidentChange
{
    None = 0,
    Opened = 1,
    Resolved = 2,
}

public interface ICheckResultRecorder
{
    /// <summary>Saves one probe result and applies it to the monitor (state, incidents, next check) in one transaction.</summary>
    Task<RecordResult> RecordAsync(ClaimedMonitor claimed, ProbeResult result, CancellationToken cancellationToken);
}

public interface IWorkerHeartbeatStore
{
    Task BeatAsync(string workerId, string version, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

using PulseWatch.Domain.Monitoring;

namespace PulseWatch.Application.Monitors;

/// <summary>Body of POST /api/v1/monitors. Only name and url are required; the rest fall back to defaults.</summary>
public sealed record CreateMonitorRequest(
    string? Name,
    string? Url,
    int? IntervalSeconds = null,
    int? TimeoutMs = null,
    int? FailureThreshold = null,
    int? RecoveryThreshold = null,
    int? ExpectedStatusMin = null,
    int? ExpectedStatusMax = null);

/// <summary>Body of PATCH /api/v1/monitors/{id}. Omitted (null) fields stay unchanged.</summary>
public sealed record UpdateMonitorRequest(
    string? Name = null,
    string? Url = null,
    int? IntervalSeconds = null,
    int? TimeoutMs = null,
    int? FailureThreshold = null,
    int? RecoveryThreshold = null,
    int? ExpectedStatusMin = null,
    int? ExpectedStatusMax = null);

public sealed record MonitorResponse(
    Guid Id,
    string Name,
    string Url,
    bool IsEnabled,
    MonitorState State,
    bool IsStale,
    int IntervalSeconds,
    int TimeoutMs,
    int FailureThreshold,
    int RecoveryThreshold,
    int ExpectedStatusMin,
    int ExpectedStatusMax,
    int ConsecutiveFailures,
    int ConsecutiveSuccesses,
    DateTimeOffset? LastCheckedAtUtc,
    CheckOutcome? LastOutcome,
    int? LastStatusCode,
    int? LastDurationMs,
    DateTimeOffset NextCheckAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Version)
{
    public static MonitorResponse From(EndpointMonitor monitor, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return new MonitorResponse(
            monitor.Id,
            monitor.Name,
            monitor.Url,
            monitor.IsEnabled,
            monitor.State,
            monitor.IsStale(nowUtc),
            monitor.IntervalSeconds,
            monitor.TimeoutMs,
            monitor.FailureThreshold,
            monitor.RecoveryThreshold,
            monitor.ExpectedStatusMin,
            monitor.ExpectedStatusMax,
            monitor.ConsecutiveFailures,
            monitor.ConsecutiveSuccesses,
            monitor.LastCheckedAtUtc,
            monitor.LastOutcome,
            monitor.LastStatusCode,
            monitor.LastDurationMs,
            monitor.NextCheckAtUtc,
            monitor.CreatedAtUtc,
            monitor.UpdatedAtUtc,
            monitor.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

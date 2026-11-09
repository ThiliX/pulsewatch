namespace PulseWatch.Application.Monitors;

/// <summary>A reporting window for uptime, latency and the chart.</summary>
public sealed record StatsWindow(string Name, TimeSpan Length, TimeSpan BucketSize)
{
    public static readonly StatsWindow Day = new("24h", TimeSpan.FromHours(24), TimeSpan.FromMinutes(5));
    public static readonly StatsWindow Week = new("7d", TimeSpan.FromDays(7), TimeSpan.FromHours(1));
    public static readonly StatsWindow Month = new("30d", TimeSpan.FromDays(30), TimeSpan.FromHours(4));

    public static IReadOnlyList<StatsWindow> All { get; } = [Day, Week, Month];

    public static StatsWindow? Parse(string? name) =>
        All.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Sampled uptime and latency for one window.
/// <para><b>Sampled uptime</b> = successful checks ÷ completed checks. It is null ("No data") when there
/// were no checks, never 100 %. <b>p95</b> is PostgreSQL's percentile_cont(0.95) over the durations of
/// successful checks only; null when there were none.</para>
/// </summary>
public sealed record UptimeWindowResponse(
    string Window,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int TotalChecks,
    int SuccessfulChecks,
    double? UptimePercent,
    int? P95DurationMs)
{
    public static double? Percent(int successful, int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(successful);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(successful, total);
        return total == 0 ? null : successful * 100d / total;
    }
}

/// <summary>GET /api/v1/monitors/{id}/stats.</summary>
public sealed record MonitorStatsResponse(IReadOnlyList<UptimeWindowResponse> Windows);

/// <summary>One chart bucket. Durations are of successful checks only (null when there were none).</summary>
public sealed record CheckSeriesPoint(
    DateTimeOffset BucketStartUtc,
    int Checks,
    int Failures,
    int? AvgDurationMs,
    int? MaxDurationMs);

/// <summary>GET /api/v1/monitors/{id}/checks/series: every bucket in the window, including empty ones.</summary>
public sealed record CheckSeriesResponse(
    string Window,
    int BucketSeconds,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<CheckSeriesPoint> Points);

/// <summary>GET /api/v1/dashboard/summary.</summary>
public sealed record DashboardSummaryResponse(
    int TotalMonitors,
    int Healthy,
    int Degraded,
    int Down,
    int Unknown,
    int Paused,
    int Stale,
    int OpenIncidents,
    DateTimeOffset? WorkerLastSeenAtUtc,
    bool WorkerOnline);

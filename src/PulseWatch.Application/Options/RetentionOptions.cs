using System.ComponentModel.DataAnnotations;

namespace PulseWatch.Application.Options;

/// <summary>
/// Check-history cleanup (section "Retention"). How long checks are kept is
/// <see cref="MonitoringOptions.CheckRetentionDays"/>; incidents and email history are never deleted by it.
/// </summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>How often the cleanup runs.</summary>
    [Range(1, 168)]
    public int IntervalHours { get; set; } = 24;

    /// <summary>Wait after the worker starts before the first run, so a restart loop can't hammer the database.</summary>
    [Range(0, 3600)]
    public int StartDelaySeconds { get; set; } = 120;

    /// <summary>Rows deleted per statement. Small batches keep locks and transactions short.</summary>
    [Range(100, 50_000)]
    public int BatchSize { get; set; } = 5000;

    /// <summary>Pause between batches, so cleanup never starves the checks.</summary>
    [Range(0, 10_000)]
    public int BatchPauseMs { get; set; } = 200;
}

namespace PulseWatch.Domain.Monitoring;

/// <summary>The monitor's current health, as shown in the UI. Stored as smallint; do not renumber.</summary>
public enum MonitorState : short
{
    /// <summary>No check result since the monitor was created, resumed or changed.</summary>
    Unknown = 0,

    /// <summary>The last check succeeded and no incident is open.</summary>
    Healthy = 1,

    /// <summary>Recent checks failed, but fewer than the failure threshold. No incident yet.</summary>
    Degraded = 2,

    /// <summary>An incident is open.</summary>
    Down = 3,

    /// <summary>The owner paused the monitor; no scheduled checks run.</summary>
    Paused = 4,
}

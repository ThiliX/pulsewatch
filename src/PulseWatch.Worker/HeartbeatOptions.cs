using System.ComponentModel.DataAnnotations;

namespace PulseWatch.Worker;

public sealed class HeartbeatOptions
{
    public const string SectionName = "Worker";

    [Range(5, 300)]
    public int HeartbeatSeconds { get; set; } = 30;

    /// <summary>Identifies this worker in worker_heartbeats. Defaults to the machine name.</summary>
    [StringLength(100)]
    public string? WorkerId { get; set; }
}

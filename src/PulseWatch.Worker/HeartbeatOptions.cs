using System.ComponentModel.DataAnnotations;

namespace PulseWatch.Worker;

public sealed class HeartbeatOptions
{
    public const string SectionName = "Worker";

    [Range(5, 300)]
    public int HeartbeatSeconds { get; set; } = 30;
}

using PulseWatch.Domain.Common;

namespace PulseWatch.Domain.Workers;

/// <summary>
/// Last time a worker process reported in. Lets the dashboard tell
/// "your endpoint is down" apart from "the PulseWatch worker is down".
/// </summary>
public sealed class WorkerHeartbeat
{
    public const int WorkerIdMaxLength = 100;
    public const int VersionMaxLength = 50;

    private WorkerHeartbeat()
    {
        WorkerId = string.Empty;
        Version = string.Empty;
    }

    public string WorkerId { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }
    public string Version { get; private set; }

    public static WorkerHeartbeat Create(string workerId, string version, DateTimeOffset nowUtc)
    {
        Guard.Utc(nowUtc);
        return new WorkerHeartbeat
        {
            WorkerId = Guard.Text(workerId, WorkerIdMaxLength),
            Version = Guard.Text(version, VersionMaxLength),
            StartedAtUtc = nowUtc,
            LastSeenAtUtc = nowUtc,
        };
    }

    public void Beat(DateTimeOffset nowUtc)
    {
        Guard.Utc(nowUtc);
        if (nowUtc > LastSeenAtUtc)
        {
            LastSeenAtUtc = nowUtc;
        }
    }
}

using PulseWatch.Domain.Common;

namespace PulseWatch.Domain.Monitoring;

/// <summary>The user-editable check settings of a monitor.</summary>
public sealed record MonitorSettings(
    int IntervalSeconds,
    int TimeoutMs,
    int FailureThreshold,
    int RecoveryThreshold,
    int ExpectedStatusMin,
    int ExpectedStatusMax)
{
    public static MonitorSettings Default { get; } = new(
        MonitorLimits.DefaultIntervalSeconds,
        MonitorLimits.DefaultTimeoutMs,
        MonitorLimits.DefaultFailureThreshold,
        MonitorLimits.DefaultRecoveryThreshold,
        MonitorLimits.DefaultExpectedStatusMin,
        MonitorLimits.DefaultExpectedStatusMax);

    /// <summary>Throws <see cref="DomainException"/> if any value is outside the hard limits.</summary>
    public void Validate()
    {
        Guard.InRange(IntervalSeconds, MonitorLimits.MinIntervalSeconds, MonitorLimits.MaxIntervalSeconds);
        Guard.InRange(TimeoutMs, MonitorLimits.MinTimeoutMs, MonitorLimits.MaxTimeoutMs);
        if (TimeoutMs >= IntervalSeconds * 1000)
        {
            throw new DomainException($"{nameof(TimeoutMs)} must be shorter than the check interval.");
        }

        Guard.InRange(FailureThreshold, MonitorLimits.MinThreshold, MonitorLimits.MaxThreshold);
        Guard.InRange(RecoveryThreshold, MonitorLimits.MinThreshold, MonitorLimits.MaxThreshold);
        Guard.InRange(ExpectedStatusMin, MonitorLimits.MinStatusCode, MonitorLimits.MaxStatusCode);
        Guard.InRange(ExpectedStatusMax, MonitorLimits.MinStatusCode, MonitorLimits.MaxStatusCode);
        if (ExpectedStatusMin > ExpectedStatusMax)
        {
            throw new DomainException($"{nameof(ExpectedStatusMin)} must not be greater than {nameof(ExpectedStatusMax)}.");
        }
    }
}

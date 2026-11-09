using PulseWatch.Domain.Monitoring;

namespace PulseWatch.UnitTests.Domain;

/// <summary>Builds monitors and check results on a fake clock that advances one interval per check.</summary>
internal sealed class MonitorTestData
{
    public static readonly DateTimeOffset Start = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private DateTimeOffset _now = Start;

    public MonitorTestData(int failureThreshold = 3, int recoveryThreshold = 1)
    {
        Monitor = EndpointMonitor.Create(
            "user-1",
            "Orders API",
            "https://api.example.com/health",
            MonitorSettings.Default with { FailureThreshold = failureThreshold, RecoveryThreshold = recoveryThreshold },
            Start);
    }

    public EndpointMonitor Monitor { get; }

    public DateTimeOffset Now => _now;

    public MonitorCheck Success(int status = 200) =>
        NextCheck(CheckOutcome.Success, status);

    public MonitorCheck Failure(CheckOutcome outcome = CheckOutcome.HttpStatusFailure, int? status = 503) =>
        NextCheck(outcome, outcome == CheckOutcome.HttpStatusFailure ? status : null);

    private MonitorCheck NextCheck(CheckOutcome outcome, int? status)
    {
        _now = _now.AddSeconds(60);
        return MonitorCheck.Create(Monitor.Id, _now, _now.AddMilliseconds(120), 120, outcome, status, null);
    }
}

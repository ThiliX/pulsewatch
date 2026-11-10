using System.Diagnostics;
using System.Diagnostics.Metrics;
using PulseWatch.Domain.Monitoring;

namespace PulseWatch.Infrastructure.Telemetry;

/// <summary>
/// The "PulseWatch" meter: what the worker is doing, for dashboards and alerts on PulseWatch itself.
/// Nothing is exported unless an exporter is configured (see <see cref="TelemetrySetup"/>); without one,
/// recording a measurement costs almost nothing.
/// <list type="table">
/// <item><term>pulsewatch.checks.completed</term><description>counter, tag <c>outcome</c></description></item>
/// <item><term>pulsewatch.check.duration</term><description>histogram (ms), tag <c>outcome</c></description></item>
/// <item><term>pulsewatch.worker.lag</term><description>histogram (s): how late a check started after it was due</description></item>
/// <item><term>pulsewatch.incidents.opened / .resolved</term><description>counters</description></item>
/// <item><term>pulsewatch.notifications.sent / .failed / .exhausted</term><description>counters (<c>failed</c> and <c>exhausted</c> tagged with <c>category</c>)</description></item>
/// <item><term>pulsewatch.outbox.pending</term><description>gauge: emails waiting to be sent or retried</description></item>
/// <item><term>pulsewatch.retention.deleted_checks</term><description>counter</description></item>
/// </list>
/// </summary>
public sealed class PulseWatchMetrics
{
    public const string MeterName = "PulseWatch";
    public const string ActivitySourceName = "PulseWatch";

    /// <summary>Spans for checks and email deliveries. Their trace id also appears in log scopes.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private readonly Counter<long> _checksCompleted;
    private readonly Histogram<double> _checkDuration;
    private readonly Histogram<double> _workerLag;
    private readonly Counter<long> _incidentsOpened;
    private readonly Counter<long> _incidentsResolved;
    private readonly Counter<long> _notificationsSent;
    private readonly Counter<long> _notificationsFailed;
    private readonly Counter<long> _notificationsExhausted;
    private readonly Counter<long> _retentionDeleted;
    private long _outboxPending;

    public PulseWatchMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        Meter = meterFactory.Create(MeterName);

        _checksCompleted = Meter.CreateCounter<long>("pulsewatch.checks.completed", "{check}", "Checks recorded, by outcome.");
        _checkDuration = Meter.CreateHistogram<double>("pulsewatch.check.duration", "ms", "Probe duration, by outcome.");
        _workerLag = Meter.CreateHistogram<double>("pulsewatch.worker.lag", "s", "How long after its due time a check was claimed.");
        _incidentsOpened = Meter.CreateCounter<long>("pulsewatch.incidents.opened", "{incident}");
        _incidentsResolved = Meter.CreateCounter<long>("pulsewatch.incidents.resolved", "{incident}");
        _notificationsSent = Meter.CreateCounter<long>("pulsewatch.notifications.sent", "{email}");
        _notificationsFailed = Meter.CreateCounter<long>("pulsewatch.notifications.failed", "{email}", "Failed attempts that will be retried, by category.");
        _notificationsExhausted = Meter.CreateCounter<long>("pulsewatch.notifications.exhausted", "{email}", "Emails given up on, by category.");
        _retentionDeleted = Meter.CreateCounter<long>("pulsewatch.retention.deleted_checks", "{check}");
        Meter.CreateObservableGauge(
            "pulsewatch.outbox.pending", () => Interlocked.Read(ref _outboxPending), "{email}", "Emails waiting to be sent or retried.");
    }

    /// <summary>The meter instance, so tests can listen to this host's measurements only.</summary>
    public Meter Meter { get; }

    public void CheckCompleted(CheckOutcome outcome, int durationMs)
    {
        var tag = new KeyValuePair<string, object?>("outcome", outcome.ToString());
        _checksCompleted.Add(1, tag);
        _checkDuration.Record(durationMs, tag);
    }

    public void CheckClaimed(TimeSpan lag) => _workerLag.Record(Math.Max(0, lag.TotalSeconds));

    public void IncidentOpened() => _incidentsOpened.Add(1);

    public void IncidentResolved() => _incidentsResolved.Add(1);

    public void NotificationSent() => _notificationsSent.Add(1);

    public void NotificationFailed(string category, bool exhausted)
    {
        var tag = new KeyValuePair<string, object?>("category", category);
        if (exhausted)
        {
            _notificationsExhausted.Add(1, tag);
        }
        else
        {
            _notificationsFailed.Add(1, tag);
        }
    }

    public void OutboxPending(long count) => Interlocked.Exchange(ref _outboxPending, count);

    public void ChecksDeleted(long count) => _retentionDeleted.Add(count);
}

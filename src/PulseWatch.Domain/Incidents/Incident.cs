using PulseWatch.Domain.Common;
using PulseWatch.Domain.Monitoring;

namespace PulseWatch.Domain.Incidents;

/// <summary>A sustained failure of one monitor, from threshold confirmation until recovery.</summary>
public sealed class Incident
{
    private Incident()
    {
    }

    public Guid Id { get; private set; }
    public Guid MonitorId { get; private set; }
    public IncidentStatus Status { get; private set; }

    /// <summary>Start of the first failed check in the streak that opened this incident.</summary>
    public DateTimeOffset FirstFailureAtUtc { get; private set; }

    /// <summary>When the failure threshold was reached and the incident was opened.</summary>
    public DateTimeOffset OpenedAtUtc { get; private set; }

    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    /// <summary>Failed checks counted in this incident, including the streak that opened it.</summary>
    public int FailureCount { get; private set; }

    public DateTimeOffset? LastFailureAtUtc { get; private set; }

    /// <summary>Evidence shown in alerts and the UI: what the failing check looked like.</summary>
    public CheckOutcome OpeningOutcome { get; private set; }

    public int? OpeningStatusCode { get; private set; }

    public bool IsOpen => Status == IncidentStatus.Open;

    internal static Incident Open(
        Guid monitorId,
        DateTimeOffset firstFailureAtUtc,
        DateTimeOffset openedAtUtc,
        int failureCount,
        CheckOutcome outcome,
        int? statusCode)
    {
        Guard.Utc(firstFailureAtUtc);
        Guard.Utc(openedAtUtc);
        if (openedAtUtc < firstFailureAtUtc)
        {
            throw new DomainException("An incident cannot open before its first failure.");
        }

        if (failureCount < 1)
        {
            throw new DomainException("An incident needs at least one failure.");
        }

        if (outcome == CheckOutcome.Success)
        {
            throw new DomainException("A successful check cannot open an incident.");
        }

        return new Incident
        {
            Id = Guid.CreateVersion7(openedAtUtc),
            MonitorId = monitorId,
            Status = IncidentStatus.Open,
            FirstFailureAtUtc = firstFailureAtUtc,
            OpenedAtUtc = openedAtUtc,
            FailureCount = failureCount,
            LastFailureAtUtc = openedAtUtc,
            OpeningOutcome = outcome,
            OpeningStatusCode = statusCode,
        };
    }

    internal void RecordFailure(DateTimeOffset atUtc)
    {
        EnsureOpen();
        Guard.Utc(atUtc);
        FailureCount++;
        LastFailureAtUtc = atUtc;
    }

    internal void Resolve(DateTimeOffset atUtc)
    {
        EnsureOpen();
        Guard.Utc(atUtc);
        if (atUtc < OpenedAtUtc)
        {
            throw new DomainException("An incident cannot resolve before it opened.");
        }

        Status = IncidentStatus.Resolved;
        ResolvedAtUtc = atUtc;
    }

    /// <summary>Outage length measured from the first failure. Open incidents are measured up to <paramref name="nowUtc"/>.</summary>
    public TimeSpan GetDuration(DateTimeOffset nowUtc) => (ResolvedAtUtc ?? nowUtc) - FirstFailureAtUtc;

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new DomainException("The incident is already resolved.");
        }
    }
}

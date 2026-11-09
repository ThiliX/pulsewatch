using PulseWatch.Domain.Common;
using PulseWatch.Domain.Incidents;

namespace PulseWatch.Domain.Monitoring;

/// <summary>
/// A user's monitored HTTP endpoint, with its schedule and health counters.
/// Named EndpointMonitor (not Monitor) to avoid clashing with System.Threading.Monitor.
/// </summary>
public sealed class EndpointMonitor
{
    private EndpointMonitor()
    {
        OwnerUserId = string.Empty;
        Name = string.Empty;
        Url = string.Empty;
    }

    public Guid Id { get; private set; }
    public string OwnerUserId { get; private set; }
    public string Name { get; private set; }
    public string Url { get; private set; }
    public bool IsEnabled { get; private set; }

    public int IntervalSeconds { get; private set; }
    public int TimeoutMs { get; private set; }
    public int FailureThreshold { get; private set; }
    public int RecoveryThreshold { get; private set; }
    public int ExpectedStatusMin { get; private set; }
    public int ExpectedStatusMax { get; private set; }

    public MonitorState State { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public int ConsecutiveSuccesses { get; private set; }

    /// <summary>Start of the first failed check in the current failure streak; null when not failing.</summary>
    public DateTimeOffset? StreakStartedAtUtc { get; private set; }

    public DateTimeOffset? LastCheckedAtUtc { get; private set; }
    public CheckOutcome? LastOutcome { get; private set; }
    public int? LastStatusCode { get; private set; }
    public int? LastDurationMs { get; private set; }

    public DateTimeOffset NextCheckAtUtc { get; private set; }

    /// <summary>Set while a worker has claimed this monitor for a check (used from Milestone 3).</summary>
    public DateTimeOffset? LeaseUntilUtc { get; private set; }

    public DateTimeOffset? ManualCheckRequestedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic concurrency token (mapped to PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public MonitorSettings Settings => new(
        IntervalSeconds, TimeoutMs, FailureThreshold, RecoveryThreshold, ExpectedStatusMin, ExpectedStatusMax);

    public static EndpointMonitor Create(
        string ownerUserId,
        string name,
        string url,
        MonitorSettings settings,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Guard.Utc(nowUtc);
        settings.Validate();

        return new EndpointMonitor
        {
            Id = Guid.CreateVersion7(nowUtc),
            OwnerUserId = Guard.Text(ownerUserId, 450),
            Name = Guard.Text(name, MonitorLimits.NameMaxLength),
            Url = MonitorUrl.Normalize(url),
            IsEnabled = true,
            IntervalSeconds = settings.IntervalSeconds,
            TimeoutMs = settings.TimeoutMs,
            FailureThreshold = settings.FailureThreshold,
            RecoveryThreshold = settings.RecoveryThreshold,
            ExpectedStatusMin = settings.ExpectedStatusMin,
            ExpectedStatusMax = settings.ExpectedStatusMax,
            State = MonitorState.Unknown,
            NextCheckAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    public bool IsExpectedStatus(int statusCode) =>
        statusCode >= ExpectedStatusMin && statusCode <= ExpectedStatusMax;

    /// <summary>Stops scheduled checks. Idempotent. An open incident stays open.</summary>
    public void Pause(DateTimeOffset nowUtc)
    {
        Guard.Utc(nowUtc);
        if (!IsEnabled)
        {
            return;
        }

        IsEnabled = false;
        State = MonitorState.Paused;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Restarts scheduled checks with a check due immediately. Idempotent.
    /// State becomes Down if an incident is still open, otherwise Unknown until the next result arrives.
    /// </summary>
    public void Resume(DateTimeOffset nowUtc, bool hasOpenIncident)
    {
        Guard.Utc(nowUtc);
        if (IsEnabled)
        {
            return;
        }

        IsEnabled = true;
        State = hasOpenIncident ? MonitorState.Down : MonitorState.Unknown;
        NextCheckAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// The incident state machine. Updates counters and state from one check result and opens or
    /// resolves an incident when a threshold is reached. Rules (see IMPLEMENTATION_PLAN.md §4.3):
    /// <list type="bullet">
    /// <item>Paused monitor: the result is recorded but counters and incidents are untouched.</item>
    /// <item>Success: resets the failure streak; resolves the open incident after RecoveryThreshold successes in a row.</item>
    /// <item>Failure: resets the success streak; opens an incident after FailureThreshold failures in a row,
    /// or adds to the already-open incident. Never opens a second incident.</item>
    /// </list>
    /// </summary>
    /// <param name="check">The new result for this monitor.</param>
    /// <param name="openIncident">This monitor's currently open incident, or null.</param>
    public CheckEvaluation ApplyCheckResult(MonitorCheck check, Incident? openIncident)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (check.MonitorId != Id)
        {
            throw new DomainException("The check belongs to a different monitor.");
        }

        if (openIncident is not null && (openIncident.MonitorId != Id || !openIncident.IsOpen))
        {
            throw new DomainException("The incident passed in is not an open incident of this monitor.");
        }

        LastCheckedAtUtc = check.CompletedAtUtc;
        LastOutcome = check.Outcome;
        LastStatusCode = check.HttpStatusCode;
        LastDurationMs = check.DurationMs;
        UpdatedAtUtc = check.CompletedAtUtc;

        if (!IsEnabled)
        {
            State = MonitorState.Paused;
            return CheckEvaluation.NoChange(openIncident);
        }

        return check.IsSuccess ? ApplySuccess(check, openIncident) : ApplyFailure(check, openIncident);
    }

    private CheckEvaluation ApplySuccess(MonitorCheck check, Incident? openIncident)
    {
        ConsecutiveFailures = 0;
        StreakStartedAtUtc = null;
        ConsecutiveSuccesses = SaturatingIncrement(ConsecutiveSuccesses);

        if (openIncident is null)
        {
            State = MonitorState.Healthy;
            return CheckEvaluation.NoChange(null);
        }

        if (ConsecutiveSuccesses >= RecoveryThreshold)
        {
            openIncident.Resolve(check.CompletedAtUtc);
            State = MonitorState.Healthy;
            return CheckEvaluation.Resolved(openIncident);
        }

        State = MonitorState.Down;
        return CheckEvaluation.NoChange(openIncident);
    }

    private CheckEvaluation ApplyFailure(MonitorCheck check, Incident? openIncident)
    {
        ConsecutiveSuccesses = 0;
        if (ConsecutiveFailures == 0)
        {
            StreakStartedAtUtc = check.StartedAtUtc;
        }

        ConsecutiveFailures = SaturatingIncrement(ConsecutiveFailures);

        if (openIncident is not null)
        {
            openIncident.RecordFailure(check.StartedAtUtc);
            State = MonitorState.Down;
            return CheckEvaluation.NoChange(openIncident);
        }

        if (ConsecutiveFailures >= FailureThreshold)
        {
            var incident = Incident.Open(
                Id,
                StreakStartedAtUtc ?? check.StartedAtUtc,
                check.CompletedAtUtc,
                ConsecutiveFailures,
                check.Outcome,
                check.HttpStatusCode);
            State = MonitorState.Down;
            return CheckEvaluation.Opened(incident);
        }

        State = MonitorState.Degraded;
        return CheckEvaluation.NoChange(null);
    }

    private static int SaturatingIncrement(int value) => value == int.MaxValue ? value : value + 1;
}

using PulseWatch.Domain.Common;

namespace PulseWatch.Domain.Monitoring;

/// <summary>One completed probe attempt. Append-only.</summary>
public sealed class MonitorCheck
{
    private MonitorCheck()
    {
    }

    public Guid Id { get; private set; }
    public Guid MonitorId { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset CompletedAtUtc { get; private set; }
    public int DurationMs { get; private set; }
    public CheckOutcome Outcome { get; private set; }
    public int? HttpStatusCode { get; private set; }

    /// <summary>Fixed, safe text chosen by our code. Never an exception message or response body.</summary>
    public string? ErrorSummary { get; private set; }

    public bool IsSuccess => Outcome == CheckOutcome.Success;

    public static MonitorCheck Create(
        Guid monitorId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        int durationMs,
        CheckOutcome outcome,
        int? httpStatusCode,
        string? errorSummary)
    {
        Guard.Utc(startedAtUtc);
        Guard.Utc(completedAtUtc);
        if (completedAtUtc < startedAtUtc)
        {
            throw new DomainException("A check cannot complete before it started.");
        }

        if (durationMs < 0)
        {
            throw new DomainException("Duration cannot be negative.");
        }

        if (httpStatusCode is { } code)
        {
            Guard.InRange(code, MonitorLimits.MinStatusCode, MonitorLimits.MaxStatusCode, nameof(httpStatusCode));
        }
        else if (outcome is CheckOutcome.Success or CheckOutcome.HttpStatusFailure)
        {
            throw new DomainException($"A {outcome} check must have an HTTP status code.");
        }

        var summary = errorSummary?.Trim();
        if (summary is { Length: > MonitorLimits.ErrorSummaryMaxLength })
        {
            summary = summary[..MonitorLimits.ErrorSummaryMaxLength];
        }

        return new MonitorCheck
        {
            Id = Guid.CreateVersion7(startedAtUtc),
            MonitorId = monitorId,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            DurationMs = durationMs,
            Outcome = outcome,
            HttpStatusCode = httpStatusCode,
            ErrorSummary = string.IsNullOrEmpty(summary) ? null : summary,
        };
    }
}

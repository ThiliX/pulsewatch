using PulseWatch.Domain.Monitoring;

namespace PulseWatch.Application.Monitors;

/// <summary>One stored check result.</summary>
public sealed record CheckResponse(
    Guid Id,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int DurationMs,
    CheckOutcome Outcome,
    int? HttpStatusCode,
    string? ErrorSummary)
{
    public static CheckResponse From(MonitorCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return new CheckResponse(
            check.Id, check.StartedAtUtc, check.CompletedAtUtc, check.DurationMs, check.Outcome, check.HttpStatusCode, check.ErrorSummary);
    }
}

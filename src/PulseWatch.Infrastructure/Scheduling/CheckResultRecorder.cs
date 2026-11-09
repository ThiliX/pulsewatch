using Microsoft.EntityFrameworkCore;
using PulseWatch.Application.Probing;
using PulseWatch.Application.Scheduling;
using PulseWatch.Domain.Incidents;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Infrastructure.Scheduling;

/// <summary>
/// Applies one probe result in a single transaction: insert the check, update the monitor's state and
/// counters, open or resolve its incident, schedule the next check and release the lease.
/// The monitor row is locked first, so an API edit or another worker can't interleave.
/// </summary>
internal sealed class CheckResultRecorder(PulseWatchDbContext db, TimeProvider time) : ICheckResultRecorder
{
    public async Task<RecordOutcome> RecordAsync(ClaimedMonitor claimed, ProbeResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimed);
        ArgumentNullException.ThrowIfNull(result);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.Database
            .ExecuteSqlAsync($"SELECT 1 FROM monitors WHERE id = {claimed.Id} FOR UPDATE", cancellationToken)
            .ConfigureAwait(false);

        var monitor = await db.Monitors.SingleOrDefaultAsync(m => m.Id == claimed.Id, cancellationToken).ConfigureAwait(false);
        if (monitor is null)
        {
            return RecordOutcome.MonitorDeleted;
        }

        if (!string.Equals(monitor.Url, claimed.Url, StringComparison.Ordinal))
        {
            // The owner changed the URL while we were checking the old one: the result is meaningless now.
            monitor.ReleaseLease();
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return RecordOutcome.UrlChanged;
        }

        var check = MonitorCheck.Create(
            monitor.Id,
            result.StartedAtUtc,
            result.CompletedAtUtc,
            result.DurationMs,
            result.Outcome,
            result.HttpStatusCode,
            result.ErrorSummary);

        var openIncident = await db.Incidents
            .SingleOrDefaultAsync(i => i.MonitorId == monitor.Id && i.Status == IncidentStatus.Open, cancellationToken)
            .ConfigureAwait(false);

        var evaluation = monitor.ApplyCheckResult(check, openIncident);
        if (evaluation.Transition == IncidentTransition.Opened)
        {
            db.Incidents.Add(evaluation.Incident!);
        }

        monitor.CompleteCheck(check.StartedAtUtc, time.GetUtcNow());
        db.MonitorChecks.Add(check);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return RecordOutcome.Recorded;
    }
}

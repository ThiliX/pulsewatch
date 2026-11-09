using Microsoft.EntityFrameworkCore;
using PulseWatch.Application.Probing;
using PulseWatch.Application.Scheduling;
using PulseWatch.Domain.Incidents;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Notifications;
using PulseWatch.Infrastructure.Notifications;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Infrastructure.Scheduling;

/// <summary>
/// Applies one probe result in a single transaction: insert the check, update the monitor's state and
/// counters, open or resolve its incident, queue the alert email (outbox), schedule the next check and
/// release the lease. Because the outbox row commits together with the incident change, an incident
/// event can't be committed without its notification, or the other way round.
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

        var now = time.GetUtcNow();
        monitor.CompleteCheck(check.StartedAtUtc, now);
        db.MonitorChecks.Add(check);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // After SaveChanges, so the incident row exists for the outbox foreign key.
        if (evaluation.Transition is IncidentTransition.Opened or IncidentTransition.Resolved)
        {
            await EnqueueNotificationAsync(monitor, evaluation, now, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return RecordOutcome.Recorded;
    }

    /// <summary>MVP: one email to the monitor's owner per incident event.</summary>
    private async Task EnqueueNotificationAsync(
        EndpointMonitor monitor, CheckEvaluation evaluation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ownerEmail = await db.Users
            .Where(u => u.Id == monitor.OwnerUserId)
            .Select(u => u.Email)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(ownerEmail))
        {
            return; // every registered user has an email; nothing to send to otherwise
        }

        var eventType = evaluation.Transition == IncidentTransition.Opened
            ? NotificationEventType.IncidentOpened
            : NotificationEventType.IncidentResolved;
        var message = NotificationOutboxMessage.Create(
            evaluation.Incident!.Id, eventType, NotificationChannel.Email, ownerEmail, now);
        await OutboxWriter.EnqueueAsync(db, message, cancellationToken).ConfigureAwait(false);
    }
}

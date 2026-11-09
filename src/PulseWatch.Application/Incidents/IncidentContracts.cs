using PulseWatch.Domain.Incidents;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Notifications;

namespace PulseWatch.Application.Incidents;

/// <summary>An incident as shown in lists.</summary>
public sealed record IncidentResponse(
    Guid Id,
    Guid MonitorId,
    string MonitorName,
    IncidentStatus Status,
    DateTimeOffset FirstFailureAtUtc,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    long DurationSeconds, // from the first failure until recovery, or until now while still open
    int FailureCount,
    DateTimeOffset? LastFailureAtUtc,
    CheckOutcome OpeningOutcome,
    int? OpeningStatusCode)
{
    public static IncidentResponse From(Incident incident, string monitorName, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(incident);
        return new IncidentResponse(
            incident.Id,
            incident.MonitorId,
            monitorName,
            incident.Status,
            incident.FirstFailureAtUtc,
            incident.OpenedAtUtc,
            incident.ResolvedAtUtc,
            (long)Math.Max(0, incident.GetDuration(nowUtc).TotalSeconds),
            incident.FailureCount,
            incident.LastFailureAtUtc,
            incident.OpeningOutcome,
            incident.OpeningStatusCode);
    }
}

/// <summary>One notification for an incident (delivery is added in Milestone 5).</summary>
public sealed record IncidentNotificationResponse(
    NotificationEventType EventType,
    NotificationChannel Channel,
    string Recipient,
    NotificationStatus Status,
    int AttemptCount,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? SentAtUtc,
    string? LastErrorCategory)
{
    public static IncidentNotificationResponse From(NotificationOutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new IncidentNotificationResponse(
            message.EventType,
            message.Channel,
            message.Recipient,
            message.Status,
            message.AttemptCount,
            message.LastAttemptAtUtc,
            message.SentAtUtc,
            message.LastErrorCategory);
    }
}

/// <summary>GET /api/v1/incidents/{id}: the incident plus its notification history.</summary>
public sealed record IncidentDetailResponse(IncidentResponse Incident, IReadOnlyList<IncidentNotificationResponse> Notifications);

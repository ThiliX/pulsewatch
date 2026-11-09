using System.Security.Claims;
using PulseWatch.Api.Auth;
using PulseWatch.Domain.Incidents;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Api.Common;

/// <summary>
/// The only way endpoints read user data. Every query starts from the signed-in user's monitors,
/// so another user's ids simply don't exist from the caller's point of view (same 404 as a missing id).
/// </summary>
internal static class Ownership
{
    public static IQueryable<EndpointMonitor> OwnedMonitors(this PulseWatchDbContext db, ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        return db.Monitors.Where(m => m.OwnerUserId == userId);
    }

    /// <summary>Incidents of the user's monitors, with the monitor name for display.</summary>
    public static IQueryable<OwnedIncident> OwnedIncidents(this PulseWatchDbContext db, ClaimsPrincipal user) =>
        from incident in db.Incidents
        join monitor in db.OwnedMonitors(user) on incident.MonitorId equals monitor.Id
        select new OwnedIncident { Incident = incident, MonitorName = monitor.Name };

    public static IResult MonitorNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Monitor not found.");

    public static IResult IncidentNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Incident not found.");
}

/// <summary>Uses init properties (not a constructor) so EF can keep composing Where/OrderBy over it.</summary>
internal sealed class OwnedIncident
{
    public required Incident Incident { get; init; }

    public required string MonitorName { get; init; }
}

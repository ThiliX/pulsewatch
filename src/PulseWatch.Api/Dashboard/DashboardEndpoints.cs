using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PulseWatch.Api.Auth;
using PulseWatch.Api.Common;
using PulseWatch.Application.Monitors;
using PulseWatch.Domain.Incidents;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Api.Dashboard;

internal static class DashboardEndpoints
{
    /// <summary>The worker beats every 30 s by default; two missed beats means it is offline.</summary>
    public static readonly TimeSpan WorkerOfflineAfter = TimeSpan.FromSeconds(60);

    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1/dashboard")
            .WithTags("Dashboard")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryFilter>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .MapGet("/summary", SummaryAsync)
            .WithSummary("Counts of my monitors by state, stale monitors, open incidents, and when a PulseWatch worker was last seen.")
            .Produces<DashboardSummaryResponse>();
    }

    private static async Task<IResult> SummaryAsync(
        ClaimsPrincipal user, PulseWatchDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        // Each user has at most MaxMonitorsPerUser (20) monitors, so loading them is cheaper than more SQL,
        // and staleness uses exactly the domain rule.
        var monitors = await db.OwnedMonitors(user).AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var openIncidents = await db.OwnedIncidents(user)
            .CountAsync(x => x.Incident.Status == IncidentStatus.Open, cancellationToken)
            .ConfigureAwait(false);
        var workerLastSeen = await db.WorkerHeartbeats
            .MaxAsync(h => (DateTimeOffset?)h.LastSeenAtUtc, cancellationToken)
            .ConfigureAwait(false);

        int Count(MonitorState state) => monitors.Count(m => m.State == state);

        return Results.Ok(new DashboardSummaryResponse(
            monitors.Count,
            Count(MonitorState.Healthy),
            Count(MonitorState.Degraded),
            Count(MonitorState.Down),
            Count(MonitorState.Unknown),
            Count(MonitorState.Paused),
            monitors.Count(m => m.IsStale(now)),
            openIncidents,
            workerLastSeen,
            workerLastSeen is { } seen && now - seen <= WorkerOfflineAfter));
    }
}

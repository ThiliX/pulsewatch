using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PulseWatch.Api.Auth;
using PulseWatch.Api.Common;
using PulseWatch.Application.Incidents;
using PulseWatch.Application.Monitors;
using PulseWatch.Domain.Incidents;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Api.Incidents;

/// <summary>All of the signed-in user's incidents, across monitors. Owner-scoped through the monitor.</summary>
internal static class IncidentEndpoints
{
    public static void MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/incidents")
            .WithTags("Incidents")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryFilter>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/", ListAsync)
            .WithSummary("My incidents, newest first. Filter with status=open or status=resolved.")
            .Produces<PagedResponse<IncidentResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One incident with its notification history.")
            .Produces<IncidentDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        PulseWatchDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken,
        string? status = null,
        int page = 1,
        int pageSize = Paging.DefaultPageSize)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        Paging.Validate(page, pageSize, errors);

        IncidentStatus? statusFilter = null;
        if (string.Equals(status, "open", StringComparison.OrdinalIgnoreCase))
        {
            statusFilter = IncidentStatus.Open;
        }
        else if (string.Equals(status, "resolved", StringComparison.OrdinalIgnoreCase))
        {
            statusFilter = IncidentStatus.Resolved;
        }
        else if (status is not null)
        {
            errors["status"] = ["Status must be 'open' or 'resolved'."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var query = db.OwnedIncidents(user);
        if (statusFilter is { } filter)
        {
            query = query.Where(x => x.Incident.Status == filter);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(x => x.Incident.OpenedAtUtc)
            .ThenByDescending(x => x.Incident.Id)
            .Page(page, pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = time.GetUtcNow();
        return Results.Ok(new PagedResponse<IncidentResponse>(
            rows.Select(r => IncidentResponse.From(r.Incident, r.MonitorName, now)).ToList(), page, pageSize, total));
    }

    private static async Task<IResult> GetAsync(
        Guid id, ClaimsPrincipal user, PulseWatchDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var row = await db.OwnedIncidents(user)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Incident.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return Ownership.IncidentNotFound();
        }

        var notifications = await db.NotificationOutbox
            .AsNoTracking()
            .Where(n => n.IncidentId == id)
            .OrderBy(n => n.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new IncidentDetailResponse(
            IncidentResponse.From(row.Incident, row.MonitorName, time.GetUtcNow()),
            notifications.Select(IncidentNotificationResponse.From).ToList()));
    }
}

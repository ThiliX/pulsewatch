using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PulseWatch.Api.Auth;
using PulseWatch.Api.Common;
using PulseWatch.Application.Incidents;
using PulseWatch.Application.Monitors;
using PulseWatch.Application.Options;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Api.Monitors;

/// <summary>Check history and incidents of one monitor. Both are owner-scoped through the monitor.</summary>
internal static class MonitorHistoryEndpoints
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);

    public static void MapMonitorHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/monitors/{id:guid}")
            .WithTags("Monitors")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryFilter>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/checks", ListChecksAsync)
            .WithSummary("Check history, newest first. Defaults to the last 24 hours; the window may span at most the retention period.")
            .Produces<PagedResponse<CheckResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/incidents", ListIncidentsAsync)
            .WithSummary("Incidents of this monitor, newest first.")
            .Produces<PagedResponse<IncidentResponse>>()
            .ProducesValidationProblem();
    }

    private static async Task<IResult> ListChecksAsync(
        Guid id,
        ClaimsPrincipal user,
        PulseWatchDbContext db,
        IOptions<MonitoringOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int page = 1,
        int pageSize = Paging.DefaultPageSize)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        Paging.Validate(page, pageSize, errors);

        var to = (toUtc ?? time.GetUtcNow()).ToUniversalTime();
        var from = (fromUtc ?? to - DefaultWindow).ToUniversalTime();
        var maxWindow = TimeSpan.FromDays(options.Value.CheckRetentionDays);
        if (from >= to)
        {
            errors["fromUtc"] = ["fromUtc must be earlier than toUtc."];
        }
        else if (to - from > maxWindow)
        {
            errors["fromUtc"] = [$"The time window can span at most {options.Value.CheckRetentionDays} days."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (!await db.OwnedMonitors(user).AnyAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return Ownership.MonitorNotFound();
        }

        var query = db.MonitorChecks
            .AsNoTracking()
            .Where(c => c.MonitorId == id && c.StartedAtUtc >= from && c.StartedAtUtc < to);

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var checks = await query
            .OrderByDescending(c => c.StartedAtUtc)
            .ThenByDescending(c => c.Id)
            .Page(page, pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new PagedResponse<CheckResponse>(checks.Select(CheckResponse.From).ToList(), page, pageSize, total));
    }

    private static async Task<IResult> ListIncidentsAsync(
        Guid id,
        ClaimsPrincipal user,
        PulseWatchDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = Paging.DefaultPageSize)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        Paging.Validate(page, pageSize, errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (!await db.OwnedMonitors(user).AnyAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return Ownership.MonitorNotFound();
        }

        var query = db.OwnedIncidents(user).Where(x => x.Incident.MonitorId == id);
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
}

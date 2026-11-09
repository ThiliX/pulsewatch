using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PulseWatch.Api.Auth;
using PulseWatch.Api.Common;
using PulseWatch.Api.RateLimiting;
using PulseWatch.Application.Monitors;
using PulseWatch.Application.Options;
using PulseWatch.Domain.Incidents;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Api.Monitors;

/// <summary>
/// Monitor endpoints. Every query goes through <see cref="Ownership.OwnedMonitors"/>, so a user can only ever see or
/// change their own monitors. Missing and not-owned ids both return the same 404.
/// </summary>
internal static class MonitorEndpoints
{
    private static readonly TimeSpan ManualCheckCooldown = TimeSpan.FromSeconds(60);

    public static void MapMonitorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/monitors")
            .WithTags("Monitors")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryFilter>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/", ListAsync)
            .WithSummary("List my monitors (paged, newest first).")
            .Produces<PagedResponse<MonitorResponse>>()
            .ProducesValidationProblem();

        group.MapPost("/", CreateAsync)
            .RequireRateLimiting(RateLimitPolicies.Writes)
            .WithSummary("Create a monitor. The URL must point to a public destination.")
            .Produces<MonitorResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Get one of my monitors.")
            .Produces<MonitorResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}", UpdateAsync)
            .RequireRateLimiting(RateLimitPolicies.Writes)
            .WithSummary("Change a monitor. Omitted fields stay unchanged. Send If-Match with the ETag to avoid overwriting someone else's change.")
            .Produces<MonitorResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireRateLimiting(RateLimitPolicies.Writes)
            .WithSummary("Delete a monitor with its check history, incidents and notifications.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/pause", PauseAsync)
            .RequireRateLimiting(RateLimitPolicies.Writes)
            .WithSummary("Stop scheduled checks. An open incident stays open.")
            .Produces<MonitorResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/resume", ResumeAsync)
            .RequireRateLimiting(RateLimitPolicies.Writes)
            .WithSummary("Restart scheduled checks; the next check is due immediately.")
            .Produces<MonitorResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/check-now", CheckNowAsync)
            .RequireRateLimiting(RateLimitPolicies.CheckNow)
            .WithSummary("Ask for a check as soon as possible. At most once per minute per monitor.")
            .Produces<MonitorResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        PulseWatchDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        string? state = null)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        Paging.Validate(page, pageSize, errors);

        MonitorState? stateFilter = null;
        if (state is not null)
        {
            if (Enum.TryParse<MonitorState>(state, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                && !int.TryParse(state, out _))
            {
                stateFilter = parsed;
            }
            else
            {
                errors["state"] = [$"State must be one of: {string.Join(", ", Enum.GetNames<MonitorState>())}."];
            }
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var query = db.OwnedMonitors(user);
        if (stateFilter is { } filter)
        {
            query = query.Where(m => m.State == filter);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var monitors = await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .ThenByDescending(m => m.Id)
            .Page(page, pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = time.GetUtcNow();
        return Results.Ok(new PagedResponse<MonitorResponse>(
            monitors.Select(m => MonitorResponse.From(m, now)).ToList(), page, pageSize, total));
    }

    private static async Task<IResult> CreateAsync(
        CreateMonitorRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        PulseWatchDbContext db,
        MonitorRequestValidator validator,
        IOptions<MonitoringOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateCreateAsync(request, cancellationToken).ConfigureAwait(false);
        if (validation.Input is not { } input)
        {
            return Results.ValidationProblem(validation.Errors);
        }

        var userId = user.GetUserId();
        var limit = options.Value.MaxMonitorsPerUser;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Lock the owner's row so two simultaneous creates can't both pass the quota check.
        await db.Database
            .ExecuteSqlAsync($"SELECT 1 FROM users WHERE id = {userId} FOR UPDATE", cancellationToken)
            .ConfigureAwait(false);

        var count = await db.Monitors.CountAsync(m => m.OwnerUserId == userId, cancellationToken).ConfigureAwait(false);
        if (count >= limit)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Monitor limit reached.",
                detail: $"You can have at most {limit} monitors. Delete one to add another.");
        }

        var now = time.GetUtcNow();
        var monitor = EndpointMonitor.Create(userId, input.Name, input.Url, input.Settings, now);
        db.Monitors.Add(monitor);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        SetETag(context, monitor);
        return Results.Created($"/api/v1/monitors/{monitor.Id}", MonitorResponse.From(monitor, now));
    }

    private static async Task<IResult> GetAsync(
        Guid id, ClaimsPrincipal user, HttpContext context, PulseWatchDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var monitor = await db.OwnedMonitors(user).SingleOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);
        if (monitor is null)
        {
            return Ownership.MonitorNotFound();
        }

        SetETag(context, monitor);
        return Results.Ok(MonitorResponse.From(monitor, time.GetUtcNow()));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateMonitorRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        PulseWatchDbContext db,
        MonitorRequestValidator validator,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var monitor = await db.OwnedMonitors(user).SingleOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);
        if (monitor is null)
        {
            return Ownership.MonitorNotFound();
        }

        if (ReadIfMatch(context) is { } ifMatch)
        {
            if (ifMatch.Version is not { } expected || expected != monitor.Version)
            {
                return VersionConflict();
            }

            // Make the UPDATE itself conditional on the version the client saw (checked atomically by PostgreSQL).
            db.Entry(monitor).Property(m => m.Version).OriginalValue = expected;
        }

        var validation = await validator.ValidateUpdateAsync(request, monitor, cancellationToken).ConfigureAwait(false);
        if (validation.Input is not { } input)
        {
            return Results.ValidationProblem(validation.Errors);
        }

        var hasOpenIncident = await HasOpenIncidentAsync(db, monitor.Id, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        monitor.UpdateDetails(input.Name, input.Url, input.Settings, hasOpenIncident, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        SetETag(context, monitor);
        return Results.Ok(MonitorResponse.From(monitor, now));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, ClaimsPrincipal user, PulseWatchDbContext db, CancellationToken cancellationToken)
    {
        // Checks, incidents and outbox rows are removed by ON DELETE CASCADE.
        var deleted = await db.OwnedMonitors(user).Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        return deleted == 0 ? Ownership.MonitorNotFound() : Results.NoContent();
    }

    private static async Task<IResult> PauseAsync(
        Guid id, ClaimsPrincipal user, HttpContext context, PulseWatchDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var monitor = await db.OwnedMonitors(user).SingleOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);
        if (monitor is null)
        {
            return Ownership.MonitorNotFound();
        }

        var now = time.GetUtcNow();
        monitor.Pause(now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        SetETag(context, monitor);
        return Results.Ok(MonitorResponse.From(monitor, now));
    }

    private static async Task<IResult> ResumeAsync(
        Guid id, ClaimsPrincipal user, HttpContext context, PulseWatchDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var monitor = await db.OwnedMonitors(user).SingleOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);
        if (monitor is null)
        {
            return Ownership.MonitorNotFound();
        }

        var hasOpenIncident = await HasOpenIncidentAsync(db, monitor.Id, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        monitor.Resume(now, hasOpenIncident);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        SetETag(context, monitor);
        return Results.Ok(MonitorResponse.From(monitor, now));
    }

    private static async Task<IResult> CheckNowAsync(
        Guid id, ClaimsPrincipal user, HttpContext context, PulseWatchDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var monitor = await db.OwnedMonitors(user).SingleOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);
        if (monitor is null)
        {
            return Ownership.MonitorNotFound();
        }

        if (!monitor.IsEnabled)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Monitor is paused.",
                detail: "Resume the monitor before requesting a check.");
        }

        var now = time.GetUtcNow();
        if (!monitor.TryRequestManualCheck(now, ManualCheckCooldown, out var retryAfter))
        {
            context.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            return Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "A check was requested recently.",
                detail: "Manual checks are limited to one per minute per monitor.");
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The worker picks it up on its next poll (Milestone 3); nothing is fetched during this request.
        SetETag(context, monitor);
        return Results.Accepted($"/api/v1/monitors/{monitor.Id}", MonitorResponse.From(monitor, now));
    }

    private static Task<bool> HasOpenIncidentAsync(PulseWatchDbContext db, Guid monitorId, CancellationToken cancellationToken) =>
        db.Incidents.AnyAsync(i => i.MonitorId == monitorId && i.Status == IncidentStatus.Open, cancellationToken);

    internal static IResult VersionConflict() =>
        Results.Problem(
            statusCode: StatusCodes.Status412PreconditionFailed,
            title: "The monitor was changed by another request.",
            detail: "Reload it and apply your change again.");

    private static void SetETag(HttpContext context, EndpointMonitor monitor) =>
        context.Response.Headers.ETag = $"\"{monitor.Version.ToString(CultureInfo.InvariantCulture)}\"";

    /// <summary>Returns null when there is no If-Match header (or it is "*"); Version is null when the header is malformed.</summary>
    private static IfMatchHeader? ReadIfMatch(HttpContext context)
    {
        var raw = context.Request.Headers.IfMatch.ToString().Trim();
        if (raw.Length == 0 || raw == "*")
        {
            return null;
        }

        if (raw.StartsWith("W/", StringComparison.Ordinal))
        {
            raw = raw[2..];
        }

        return uint.TryParse(raw.Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? new IfMatchHeader(version)
            : new IfMatchHeader(null);
    }

    private sealed record IfMatchHeader(uint? Version);
}

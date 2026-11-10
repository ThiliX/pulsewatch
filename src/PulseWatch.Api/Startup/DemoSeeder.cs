using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PulseWatch.Domain.Incidents;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Notifications;
using PulseWatch.Infrastructure.Identity;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Api.Startup;

/// <summary>
/// Creates a demo account with four monitors and a week of history, so the dashboard has something to show.
/// The history is replayed through the real domain state machine (<see cref="EndpointMonitor.ApplyCheckResult"/>),
/// so monitor states, incidents and email history are exactly what PulseWatch would have produced.
/// Targets are the IANA example domains, which exist for this purpose. Running it again replaces the
/// demo account's monitors and gives it a new password.
/// </summary>
internal sealed class DemoSeeder(PulseWatchDbContext db, UserManager<ApplicationUser> users, TimeProvider time)
{
    public const string DemoEmail = "demo@pulsewatch.localhost";
    public static readonly TimeSpan History = TimeSpan.FromDays(7);
    public const int IntervalSeconds = 300;

    public sealed record SeedResult(string Email, string Password, int Monitors, int Checks, int Incidents);

    /// <summary>A demo monitor and the failures in its history, as (minutes ago, number of failed checks).</summary>
    private sealed record Spec(string Name, string Url, int BaseLatencyMs, CheckOutcome FailureOutcome, (int MinutesAgo, int Count)[] Failures);

    private static readonly Spec[] Specs =
    [
        new("Marketing site (demo)", "https://example.com/", 120, CheckOutcome.HttpStatusFailure, []),
        // Two failures in a row: Degraded for a while, but below the threshold of 3, so no incident.
        new("Docs site (demo)", "https://example.org/", 260, CheckOutcome.Timeout, [(3 * 24 * 60, 2)]),
        // A 30-minute outage two days ago: one incident, a "down" and a "recovered" email.
        new("Payments API (demo)", "https://example.net/", 180, CheckOutcome.HttpStatusFailure, [(2 * 24 * 60, 6)]),
        new("Example domains page (demo)", "https://www.iana.org/help/example-domains", 420, CheckOutcome.ConnectionFailure,
            [(5 * 24 * 60, 12), (20 * 60, 4)]),
    ];

    public async Task<SeedResult> SeedAsync(CancellationToken cancellationToken)
    {
        var now = TruncateToMilliseconds(time.GetUtcNow());
        var password = RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789", 20);
        var user = await PrepareUserAsync(password, now).ConfigureAwait(false);

        int checks = 0, incidents = 0;
        foreach (var (spec, index) in Specs.Select((s, i) => (s, i)))
        {
            var (c, n) = AddMonitorWithHistory(user.Id, spec, now, seed: index + 1);
            checks += c;
            incidents += n;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new SeedResult(DemoEmail, password, Specs.Length, checks, incidents);
    }

    private async Task<ApplicationUser> PrepareUserAsync(string password, DateTimeOffset now)
    {
        var user = await users.FindByEmailAsync(DemoEmail).ConfigureAwait(false);
        if (user is null)
        {
            user = new ApplicationUser { UserName = DemoEmail, Email = DemoEmail, CreatedAtUtc = now };
            ThrowIfFailed(await users.CreateAsync(user, password).ConfigureAwait(false));
            return user;
        }

        // Start over: deleting the monitors cascades to their checks, incidents and emails.
        await db.Monitors.Where(m => m.OwnerUserId == user.Id).ExecuteDeleteAsync().ConfigureAwait(false);
        ThrowIfFailed(await users.RemovePasswordAsync(user).ConfigureAwait(false));
        ThrowIfFailed(await users.AddPasswordAsync(user, password).ConfigureAwait(false));
        return user;
    }

    private (int Checks, int Incidents) AddMonitorWithHistory(string ownerId, Spec spec, DateTimeOffset now, int seed)
    {
        var random = new Random(seed); // deterministic, so every seeded demo looks the same
        var start = now - History;
        var monitor = EndpointMonitor.Create(
            ownerId, spec.Name, spec.Url, MonitorSettings.Default with { IntervalSeconds = IntervalSeconds }, start.AddMinutes(-1));
        db.Monitors.Add(monitor);

        var failingSlots = new HashSet<int>();
        foreach (var (minutesAgo, count) in spec.Failures)
        {
            var first = (int)((History.TotalMinutes - minutesAgo) * 60 / IntervalSeconds);
            for (var i = 0; i < count; i++)
            {
                failingSlots.Add(first + i);
            }
        }

        Incident? open = null;
        int checks = 0, incidents = 0;
        for (var slot = 0; ; slot++)
        {
            var started = start.AddSeconds(slot * IntervalSeconds);
            if (started >= now)
            {
                break;
            }

            var check = failingSlots.Contains(slot)
                ? Failure(monitor.Id, started, spec.FailureOutcome)
                : Success(monitor.Id, started, Latency(spec.BaseLatencyMs, started, random));
            db.MonitorChecks.Add(check);
            checks++;

            var evaluation = monitor.ApplyCheckResult(check, open);
            if (evaluation.Transition == IncidentTransition.Opened)
            {
                open = evaluation.Incident!;
                db.Incidents.Add(open);
                incidents++;
                AddSentEmail(open.Id, NotificationEventType.IncidentOpened, check.CompletedAtUtc);
            }
            else if (evaluation.Transition == IncidentTransition.Resolved)
            {
                AddSentEmail(open!.Id, NotificationEventType.IncidentResolved, check.CompletedAtUtc);
                open = null;
            }

            monitor.CompleteCheck(check.StartedAtUtc, check.CompletedAtUtc);
        }

        return (checks, incidents);
    }

    private void AddSentEmail(Guid incidentId, NotificationEventType eventType, DateTimeOffset at)
    {
        var message = NotificationOutboxMessage.Create(incidentId, eventType, NotificationChannel.Email, DemoEmail, at);
        message.BeginAttempt(at.AddSeconds(2), at.AddMinutes(3));
        message.MarkSent(at.AddSeconds(3));
        db.NotificationOutbox.Add(message);
    }

    /// <summary>Base latency with a daily rhythm, ±15 % noise and an occasional slow response.</summary>
    private static int Latency(int baseMs, DateTimeOffset at, Random random)
    {
        var daily = 1 + (0.25 * Math.Sin(2 * Math.PI * at.TimeOfDay.TotalHours / 24));
        var noise = 0.85 + (random.NextDouble() * 0.3);
        var spike = random.NextDouble() < 0.01 ? 3 : 1;
        return (int)(baseMs * daily * noise * spike);
    }

    private static MonitorCheck Success(Guid monitorId, DateTimeOffset started, int durationMs) =>
        MonitorCheck.Create(monitorId, started, started.AddMilliseconds(durationMs), durationMs, CheckOutcome.Success, 200, null);

    private static MonitorCheck Failure(Guid monitorId, DateTimeOffset started, CheckOutcome outcome) => outcome switch
    {
        CheckOutcome.Timeout => MonitorCheck.Create(
            monitorId, started, started.AddSeconds(10), 10_000, outcome, null, "The request timed out."),
        CheckOutcome.HttpStatusFailure => MonitorCheck.Create(
            monitorId, started, started.AddMilliseconds(90), 90, outcome, 503, "Unexpected HTTP status 503."),
        _ => MonitorCheck.Create(monitorId, started, started.AddMilliseconds(40), 40, outcome, null, "Connection failed."),
    };

    private static DateTimeOffset TruncateToMilliseconds(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not set up the demo account: " + string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}

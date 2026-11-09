using PulseWatch.Domain.Monitoring;
using PulseWatch.Infrastructure.Identity;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.IntegrationTests.Infrastructure;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    public static async Task<ApplicationUser> AddUserAsync(PulseWatchDbContext db, string? email = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            CreatedAtUtc = Now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static async Task<EndpointMonitor> AddMonitorAsync(
        PulseWatchDbContext db, string ownerUserId, MonitorSettings? settings = null)
    {
        var monitor = EndpointMonitor.Create(
            ownerUserId, "Orders API", "https://api.example.com/health", settings ?? MonitorSettings.Default, Now);
        db.Monitors.Add(monitor);
        await db.SaveChangesAsync();
        return monitor;
    }

    public static MonitorCheck Failure(EndpointMonitor monitor, DateTimeOffset at) =>
        MonitorCheck.Create(monitor.Id, at, at.AddMilliseconds(80), 80, CheckOutcome.HttpStatusFailure, 503, null);

    public static MonitorCheck Success(EndpointMonitor monitor, DateTimeOffset at) =>
        MonitorCheck.Create(monitor.Id, at, at.AddMilliseconds(80), 80, CheckOutcome.Success, 200, null);
}

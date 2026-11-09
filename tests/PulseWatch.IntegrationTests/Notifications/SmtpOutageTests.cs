using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseWatch.Application.Notifications;
using PulseWatch.Application.Probing;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Notifications;
using PulseWatch.IntegrationTests.Infrastructure;
using PulseWatch.Worker;

namespace PulseWatch.IntegrationTests.Notifications;

/// <summary>
/// The full worker host with the real SMTP sender pointed at a port where nothing listens:
/// checks and incidents keep being recorded while the email waits for retry.
/// </summary>
public class SmtpOutageTests(PostgresFixture postgres)
{
    private const string SmtpPassword = "Sm7p-Pa55word-Never-Logged!";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class AlwaysDownProbe : IProbeRunner
    {
        public Task<ProbeResult> ProbeAsync(ProbeRequest request, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ProbeResult(now, now.AddMilliseconds(10), 10, CheckOutcome.HttpStatusFailure, 503, "503"));
        }
    }

    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for: {what}");
            await Task.Delay(200, Ct);
        }
    }

    [Fact]
    public async Task Smtp_outage_does_not_affect_check_recording_and_the_password_never_reaches_the_logs()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        Guid monitorId;
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var user = await TestData.AddUserAsync(db);
            var monitor = EndpointMonitor.Create(user.Id, "Down API", "https://down.example.com/",
                MonitorSettings.Default with { FailureThreshold = 1 }, DateTimeOffset.UtcNow.AddMinutes(-1));
            db.Monitors.Add(monitor);
            await db.SaveChangesAsync(Ct);
            monitorId = monitor.Id;
        }

        var logs = new CapturingLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(logs);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PulseWatch"] = connectionString,
            ["Monitoring:PollIntervalSeconds"] = "1",
            ["Notifications:Provider"] = "Smtp",
            ["Notifications:PollIntervalSeconds"] = "1",
            ["Notifications:SendTimeoutSeconds"] = "5",
            ["Email:Host"] = "127.0.0.1",
            ["Email:Port"] = ClosedPort().ToString(CultureInfo.InvariantCulture),
            ["Email:UseStartTls"] = "false",
            ["Email:Username"] = "pulse",
            ["Email:Password"] = SmtpPassword,
            ["Email:FromAddress"] = "alerts@pulsewatch.test",
        });
        builder.Services.AddPulseWatchWorker(builder.Configuration);
        builder.Services.RemoveAll<IProbeRunner>();
        builder.Services.AddSingleton<IProbeRunner, AlwaysDownProbe>();

        using var worker = builder.Build();
        await worker.StartAsync(Ct);
        try
        {
            // The first check opens the incident and queues the email; sending it fails.
            await WaitUntilAsync(async () =>
            {
                await using var db = PostgresFixture.CreateContext(connectionString);
                return await db.NotificationOutbox.AnyAsync(m => m.Status == NotificationStatus.Failed, Ct);
            }, "the email to fail");

            // Checks keep being recorded during the outage.
            await using (var db = PostgresFixture.CreateContext(connectionString))
            {
                await db.Database.ExecuteSqlAsync(
                    $"UPDATE monitors SET next_check_at_utc = now() - interval '1 second' WHERE id = {monitorId}", Ct);
            }

            await WaitUntilAsync(async () =>
            {
                await using var db = PostgresFixture.CreateContext(connectionString);
                return await db.MonitorChecks.CountAsync(c => c.MonitorId == monitorId, Ct) >= 2;
            }, "a second check");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        await using var read = PostgresFixture.CreateContext(connectionString);
        var message = await read.NotificationOutbox.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(NotificationStatus.Failed, message.Status);
        Assert.Equal(EmailErrorCategories.ConnectionFailed, message.LastErrorCategory);
        Assert.Equal(1, message.AttemptCount);
        Assert.True(message.NextAttemptAtUtc > message.LastAttemptAtUtc);
        Assert.Equal(1, await read.Incidents.CountAsync(Ct));
        Assert.Equal(MonitorState.Down, (await read.Monitors.SingleAsync(Ct)).State);

        Assert.Contains(logs.Lines, l => l.Contains("ConnectionFailed", StringComparison.Ordinal));
        Assert.DoesNotContain(SmtpPassword, logs.AllText, StringComparison.Ordinal);
    }
}

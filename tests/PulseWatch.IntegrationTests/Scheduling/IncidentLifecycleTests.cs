using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseWatch.Application.Security;
using PulseWatch.Domain.Incidents;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Notifications;
using PulseWatch.IntegrationTests.Infrastructure;
using PulseWatch.Worker;

namespace PulseWatch.IntegrationTests.Scheduling;

/// <summary>
/// Milestone 4 exit gate: a real worker (real probe engine, real HTTP over a socket) checks a local
/// endpoint that the test switches between healthy and failing. The monitor is created through the
/// real API, and the outcome is read back through the API.
/// </summary>
public class IncidentLifecycleTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IHost BuildWorker(string connectionString, int port)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PulseWatch"] = connectionString,
            ["Monitoring:PollIntervalSeconds"] = "1",
            ["Notifications:PollIntervalSeconds"] = "1",
            ["Monitoring:AllowedPorts:0"] = port.ToString(CultureInfo.InvariantCulture),
            ["Worker:WorkerId"] = "lifecycle-worker",
        });
        builder.Services.AddPulseWatchWorker(builder.Configuration);

        // Test-only: let the probe reach the local server. Production has no switch for this.
        builder.Services.RemoveAll<IDnsResolver>();
        builder.Services.AddSingleton<IDnsResolver, LoopbackDns>();
        builder.Services.RemoveAll<IAddressPolicy>();
        builder.Services.AddSingleton<IAddressPolicy, LoopbackAllowedPolicy>();
        return builder.Build();
    }

    /// <summary>Makes the monitor due now and waits until the worker has stored check number <paramref name="expectedChecks"/>.</summary>
    private static async Task<MonitorCheck> RunCheckAsync(string connectionString, Guid monitorId, int expectedChecks)
    {
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            await db.Database.ExecuteSqlAsync(
                $"UPDATE monitors SET next_check_at_utc = now() - interval '1 second' WHERE id = {monitorId}", Ct);
        }

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            await using var db = PostgresFixture.CreateContext(connectionString);
            var checks = await db.MonitorChecks.AsNoTracking().Where(c => c.MonitorId == monitorId)
                .OrderBy(c => c.StartedAtUtc).ToListAsync(Ct);
            var monitor = await db.Monitors.AsNoTracking().SingleAsync(m => m.Id == monitorId, Ct);
            if (checks.Count == expectedChecks && monitor.LeaseUntilUtc is null)
            {
                return checks[^1];
            }

            Assert.True(checks.Count <= expectedChecks, $"Expected {expectedChecks} checks but found {checks.Count}.");
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for check {expectedChecks}.");
            await Task.Delay(150, Ct);
        }
    }

    private static async Task<(EndpointMonitor Monitor, List<Incident> Incidents)> StateAsync(string connectionString, Guid monitorId)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        var monitor = await db.Monitors.AsNoTracking().SingleAsync(m => m.Id == monitorId, Ct);
        var incidents = await db.Incidents.AsNoTracking().Where(i => i.MonitorId == monitorId).ToListAsync(Ct);
        return (monitor, incidents);
    }

    [Fact]
    public async Task Healthy_to_failing_to_recovered_produces_exactly_one_incident_lifecycle_even_across_a_worker_restart()
    {
        await using var target = new SwitchableHttpServer();
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var api = new ApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["Monitoring:AllowedPorts:0"] = target.Port.ToString(CultureInfo.InvariantCulture),
        });
        using var owner = await api.CreateSignedInClientAsync();
        using var created = await owner.Http.PostAsJsonAsync("/api/v1/monitors", new
        {
            name = "Switchable target",
            url = $"http://target.test:{target.Port}/health",
            failureThreshold = 3,
            recoveryThreshold = 2,
        }, Ct);
        created.EnsureSuccessStatusCode();
        var monitorId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var worker = BuildWorker(connectionString, target.Port);
        await worker.StartAsync(Ct);
        try
        {
            // 1. Healthy.
            target.Status = 200;
            var check1 = await RunCheckAsync(connectionString, monitorId, 1);
            Assert.Equal(CheckOutcome.Success, check1.Outcome);
            var (monitor, incidents) = await StateAsync(connectionString, monitorId);
            Assert.Equal(MonitorState.Healthy, monitor.State);
            Assert.Empty(incidents);

            // 2. Two failures: below the threshold of 3, so degraded but no incident.
            target.Status = 500;
            var check2 = await RunCheckAsync(connectionString, monitorId, 2);
            await RunCheckAsync(connectionString, monitorId, 3);
            (monitor, incidents) = await StateAsync(connectionString, monitorId);
            Assert.Equal(CheckOutcome.HttpStatusFailure, check2.Outcome);
            Assert.Equal(500, check2.HttpStatusCode);
            Assert.Equal(MonitorState.Degraded, monitor.State);
            Assert.Empty(incidents);

            // 3. Third failure opens exactly one incident, dated from the FIRST failure.
            var check4 = await RunCheckAsync(connectionString, monitorId, 4);
            (monitor, incidents) = await StateAsync(connectionString, monitorId);
            var incident = Assert.Single(incidents);
            Assert.Equal(IncidentStatus.Open, incident.Status);
            Assert.Equal(check2.StartedAtUtc, incident.FirstFailureAtUtc);
            Assert.Equal(check4.CompletedAtUtc, incident.OpenedAtUtc);
            Assert.Equal(3, incident.FailureCount);
            Assert.Equal(500, incident.OpeningStatusCode);
            Assert.Equal(MonitorState.Down, monitor.State);

            // 4. More failures add to the same incident.
            await RunCheckAsync(connectionString, monitorId, 5);

            // 5. Restart the worker in the middle of the incident.
            await worker.StopAsync(Ct);
            worker.Dispose();
            worker = BuildWorker(connectionString, target.Port);
            await worker.StartAsync(Ct);

            await RunCheckAsync(connectionString, monitorId, 6);
            (_, incidents) = await StateAsync(connectionString, monitorId);
            incident = Assert.Single(incidents);
            Assert.Equal(IncidentStatus.Open, incident.Status);
            Assert.Equal(5, incident.FailureCount);

            // 6. Recovery threshold is 2: the first success is not enough.
            target.Status = 200;
            await RunCheckAsync(connectionString, monitorId, 7);
            (monitor, incidents) = await StateAsync(connectionString, monitorId);
            Assert.Equal(IncidentStatus.Open, Assert.Single(incidents).Status);
            Assert.Equal(MonitorState.Down, monitor.State);

            // 7. The second success in a row resolves it, once.
            var check8 = await RunCheckAsync(connectionString, monitorId, 8);
            await RunCheckAsync(connectionString, monitorId, 9);
            (monitor, incidents) = await StateAsync(connectionString, monitorId);
            incident = Assert.Single(incidents);
            Assert.Equal(IncidentStatus.Resolved, incident.Status);
            Assert.Equal(check8.CompletedAtUtc, incident.ResolvedAtUtc);
            Assert.Equal(MonitorState.Healthy, monitor.State);

            // 8. Exactly two emails (opened + resolved), both delivered by the dispatcher (Logging provider).
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (true)
            {
                await using var db = PostgresFixture.CreateContext(connectionString);
                var statuses = await db.NotificationOutbox.AsNoTracking()
                    .Where(n => n.IncidentId == incident.Id).Select(n => n.Status).ToListAsync(Ct);
                Assert.True(statuses.Count <= 2, $"Expected at most 2 notifications but found {statuses.Count}.");
                if (statuses.Count == 2 && statuses.All(s => s == NotificationStatus.Sent))
                {
                    break;
                }

                Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for both notifications to be sent.");
                await Task.Delay(150, Ct);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }

        // 9. The API shows the same story.
        var apiIncidents = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/monitors/{monitorId}/incidents", Ct);
        var apiIncident = Assert.Single(apiIncidents.GetProperty("items").EnumerateArray());
        Assert.Equal("Resolved", apiIncident.GetProperty("status").GetString());
        Assert.Equal(5, apiIncident.GetProperty("failureCount").GetInt32());
        Assert.True(apiIncident.GetProperty("durationSeconds").GetInt64() >= 0);

        var apiChecks = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/monitors/{monitorId}/checks", Ct);
        Assert.Equal(9, apiChecks.GetProperty("totalCount").GetInt32());
        Assert.Equal("Success", apiChecks.GetProperty("items")[0].GetProperty("outcome").GetString());

        var openNow = await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/incidents?status=open", Ct);
        Assert.Equal(0, openNow.GetProperty("totalCount").GetInt32());

        var detail = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/v1/incidents/{apiIncident.GetProperty("id").GetGuid()}", Ct);
        var notifications = detail.GetProperty("notifications").EnumerateArray().ToList();
        Assert.Equal(["IncidentOpened", "IncidentResolved"], notifications.Select(n => n.GetProperty("eventType").GetString()));
        Assert.All(notifications, n =>
        {
            Assert.Equal("Sent", n.GetProperty("status").GetString());
            Assert.Equal(1, n.GetProperty("attemptCount").GetInt32());
        });
    }
}

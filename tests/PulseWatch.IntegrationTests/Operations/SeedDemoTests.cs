using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PulseWatch.Api.Startup;
using PulseWatch.Domain.Incidents;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Notifications;
using PulseWatch.IntegrationTests.Infrastructure;

namespace PulseWatch.IntegrationTests.Operations;

public class SeedDemoTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<DemoSeeder.SeedResult> SeedAsync(ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<DemoSeeder>(scope.ServiceProvider).SeedAsync(Ct);
    }

    [Fact]
    public async Task Seeds_a_consistent_week_of_history_and_can_be_run_again()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new ApiFactory(connectionString);

        var first = await SeedAsync(factory);
        var second = await SeedAsync(factory);

        Assert.Equal(DemoSeeder.DemoEmail, second.Email);
        Assert.NotEqual(first.Password, second.Password);
        Assert.Equal(4, second.Monitors);
        Assert.Equal(first.Checks, second.Checks);

        await using var db = PostgresFixture.CreateContext(connectionString);
        // Running it twice replaced the data instead of doubling it.
        Assert.Equal(4, await db.Monitors.CountAsync(Ct));
        Assert.Equal(second.Checks, await db.MonitorChecks.CountAsync(Ct));
        Assert.InRange(second.Checks, 4 * 2000, 4 * 2020); // 7 days at 5-minute intervals

        // Replayed through the state machine: incidents are resolved, each with a "down" and a "recovered" email.
        var incidents = await db.Incidents.AsNoTracking().ToListAsync(Ct);
        Assert.Equal(second.Incidents, incidents.Count);
        Assert.Equal(3, incidents.Count);
        Assert.All(incidents, i => Assert.Equal(IncidentStatus.Resolved, i.Status));
        Assert.Equal(6, await db.NotificationOutbox.CountAsync(m => m.Status == NotificationStatus.Sent, Ct));
        Assert.All(await db.Monitors.AsNoTracking().ToListAsync(Ct), m => Assert.Equal(MonitorState.Healthy, m.State));

        // The printed credentials work, and the dashboard shows real numbers.
        using var client = await factory.CreateTestClientAsync();
        using var login = await client.Http.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = second.Email, password = second.Password }, Ct);
        login.EnsureSuccessStatusCode();
        var summary = await client.Http.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/summary", Ct);
        Assert.Equal(4, summary.GetProperty("totalMonitors").GetInt32());
        Assert.Equal(4, summary.GetProperty("healthy").GetInt32());

        var payments = await db.Monitors.AsNoTracking().SingleAsync(m => m.Name == "Payments API (demo)", Ct);
        var stats = await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/monitors/{payments.Id}/stats", Ct);
        var week = stats.GetProperty("windows")[1];
        Assert.Equal(6, week.GetProperty("totalChecks").GetInt32() - week.GetProperty("successfulChecks").GetInt32());
    }

    [Fact]
    public async Task Is_refused_outside_Development()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        await using var app = builder.Build();
        using var output = new StringWriter();

        var exitCode = await SeedDemoCommand.RunAsync(app, output);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
    }
}

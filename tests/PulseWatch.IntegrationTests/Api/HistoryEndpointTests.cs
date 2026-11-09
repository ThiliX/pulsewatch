using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Notifications;
using PulseWatch.IntegrationTests.Infrastructure;

namespace PulseWatch.IntegrationTests.Api;

public class HistoryEndpointTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string _connectionString = string.Empty;

    private async Task<(ApiFactory Factory, TestClient Client, Guid MonitorId)> SetUpAsync(int failureThreshold = 1)
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        var factory = new ApiFactory(_connectionString);
        var client = await factory.CreateSignedInClientAsync();
        using var created = await client.Http.PostAsJsonAsync(
            "/api/v1/monitors", new { name = "Orders API", url = "https://api.example.com/", failureThreshold }, Ct);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        return (factory, client, id);
    }

    private static DateTimeOffset Ms(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);

    private async Task AddChecksAsync(Guid monitorId, params TimeSpan[] agesAgo)
    {
        await using var db = PostgresFixture.CreateContext(_connectionString);
        var now = Ms(DateTimeOffset.UtcNow);
        foreach (var age in agesAgo)
        {
            var started = now - age;
            db.MonitorChecks.Add(MonitorCheck.Create(monitorId, started, started.AddMilliseconds(50), 50, CheckOutcome.Success, 200, null));
        }

        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Checks_default_to_the_last_24_hours_newest_first_and_are_paged()
    {
        var (factory, client, id) = await SetUpAsync();
        await using var _ = factory;
        using var __ = client;
        await AddChecksAsync(id, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3), TimeSpan.FromHours(30));

        var page1 = await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/monitors/{id}/checks?pageSize=2", Ct);
        var page2 = await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/monitors/{id}/checks?pageSize=2&page=2", Ct);

        Assert.Equal(3, page1.GetProperty("totalCount").GetInt32()); // the 30-hour-old check is outside the default window
        var times = page1.GetProperty("items").EnumerateArray().Concat(page2.GetProperty("items").EnumerateArray())
            .Select(c => c.GetProperty("startedAtUtc").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);
        var first = page1.GetProperty("items")[0];
        Assert.Equal("Success", first.GetProperty("outcome").GetString());
        Assert.Equal(200, first.GetProperty("httpStatusCode").GetInt32());
    }

    [Fact]
    public async Task Checks_can_be_filtered_by_an_explicit_window()
    {
        var (factory, client, id) = await SetUpAsync();
        await using var _ = factory;
        using var __ = client;
        await AddChecksAsync(id, TimeSpan.FromHours(1), TimeSpan.FromHours(30), TimeSpan.FromHours(50));
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-60).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-20).ToString("O"));

        var window = await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/monitors/{id}/checks?fromUtc={from}&toUtc={to}", Ct);

        Assert.Equal(2, window.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("fromUtc=2026-11-01T00:00:00Z&toUtc=2026-11-01T00:00:00Z")]   // empty window
    [InlineData("fromUtc=2026-11-02T00:00:00Z&toUtc=2026-11-01T00:00:00Z")]   // reversed
    [InlineData("fromUtc=2026-09-01T00:00:00Z&toUtc=2026-11-01T00:00:00Z")]   // longer than retention
    [InlineData("pageSize=500")]
    public async Task Invalid_check_windows_and_paging_are_rejected(string query)
    {
        var (factory, client, id) = await SetUpAsync();
        await using var _ = factory;
        using var __ = client;

        using var response = await client.Http.GetAsync($"/api/v1/monitors/{id}/checks?{query}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Incidents_are_listed_per_monitor_and_globally_with_status_filter_and_details()
    {
        var (factory, client, id) = await SetUpAsync(failureThreshold: 1);
        await using var _ = factory;
        using var __ = client;
        Guid resolvedId, openId;
        var start = Ms(DateTimeOffset.UtcNow.AddHours(-2));
        await using (var db = PostgresFixture.CreateContext(_connectionString))
        {
            var monitor = await db.Monitors.SingleAsync(m => m.Id == id, Ct);
            var resolved = monitor.ApplyCheckResult(TestData.Failure(monitor, start), null).Incident!;
            db.Incidents.Add(resolved);
            await db.SaveChangesAsync(Ct);
            monitor.ApplyCheckResult(TestData.Success(monitor, start.AddMinutes(10)), resolved);
            var open = monitor.ApplyCheckResult(TestData.Failure(monitor, start.AddMinutes(60)), null).Incident!;
            db.Incidents.Add(open);
            db.NotificationOutbox.Add(NotificationOutboxMessage.Create(
                open.Id, NotificationEventType.IncidentOpened, NotificationChannel.Email, client.Email, start.AddMinutes(60)));
            await db.SaveChangesAsync(Ct);
            (resolvedId, openId) = (resolved.Id, open.Id);
        }

        var perMonitor = await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/monitors/{id}/incidents", Ct);
        var openOnly = await client.Http.GetFromJsonAsync<JsonElement>("/api/v1/incidents?status=open", Ct);
        var resolvedOnly = await client.Http.GetFromJsonAsync<JsonElement>("/api/v1/incidents?status=RESOLVED", Ct);
        var detail = await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/incidents/{openId}", Ct);
        using var badFilter = await client.Http.GetAsync("/api/v1/incidents?status=burning", Ct);

        Assert.Equal([openId, resolvedId], perMonitor.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        Assert.Equal([openId], openOnly.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        var resolvedItem = Assert.Single(resolvedOnly.GetProperty("items").EnumerateArray());
        Assert.Equal("Resolved", resolvedItem.GetProperty("status").GetString());
        Assert.Equal("Orders API", resolvedItem.GetProperty("monitorName").GetString());
        Assert.Equal(600 + 0, resolvedItem.GetProperty("durationSeconds").GetInt64()); // first failure -> recovery = 10 min
        Assert.Equal("HttpStatusFailure", resolvedItem.GetProperty("openingOutcome").GetString());
        Assert.Equal(503, resolvedItem.GetProperty("openingStatusCode").GetInt32());

        Assert.Equal("Open", detail.GetProperty("incident").GetProperty("status").GetString());
        Assert.True(detail.GetProperty("incident").GetProperty("durationSeconds").GetInt64() >= 3600);
        var notification = Assert.Single(detail.GetProperty("notifications").EnumerateArray());
        Assert.Equal("IncidentOpened", notification.GetProperty("eventType").GetString());
        Assert.Equal("Pending", notification.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, badFilter.StatusCode);
    }
}

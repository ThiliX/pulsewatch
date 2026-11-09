using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PulseWatch.Application.Monitors;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Workers;
using PulseWatch.Infrastructure.Metrics;
using PulseWatch.IntegrationTests.Infrastructure;

namespace PulseWatch.IntegrationTests.Api;

/// <summary>Uptime, p95, chart series and the dashboard summary, on known data sets.</summary>
public class MetricsEndpointTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = TestData.Now; // 12:00:00 UTC, aligned to every bucket size

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MonitorCheck Check(Guid monitorId, TimeSpan ago, CheckOutcome outcome, int durationMs)
    {
        var started = Now - ago;
        int? status = outcome switch
        {
            CheckOutcome.Success => 200,
            CheckOutcome.HttpStatusFailure => 503,
            _ => null,
        };
        return MonitorCheck.Create(monitorId, started, started.AddMilliseconds(durationMs), durationMs, outcome, status, null);
    }

    /// <summary>
    /// 24 h: 100 ms ✓, 200 ms ✓, timeout ✗. 7 d adds 300 ms ✓ and a 503 ✗. 30 d adds 400 ms ✓.
    /// One check 31 days old is outside every window.
    /// </summary>
    private static async Task<(string ConnectionString, Guid MonitorId)> SeedKnownDataAsync(PostgresFixture postgres)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var user = await TestData.AddUserAsync(db);
        var monitor = await TestData.AddMonitorAsync(db, user.Id);
        db.MonitorChecks.AddRange(
            Check(monitor.Id, TimeSpan.FromHours(1), CheckOutcome.Success, 100),
            Check(monitor.Id, TimeSpan.FromHours(2), CheckOutcome.Success, 200),
            Check(monitor.Id, TimeSpan.FromHours(3), CheckOutcome.Timeout, 10_000),
            Check(monitor.Id, TimeSpan.FromDays(3), CheckOutcome.Success, 300),
            Check(monitor.Id, TimeSpan.FromDays(3) + TimeSpan.FromMinutes(1), CheckOutcome.HttpStatusFailure, 50),
            Check(monitor.Id, TimeSpan.FromDays(20), CheckOutcome.Success, 400),
            Check(monitor.Id, TimeSpan.FromDays(31), CheckOutcome.Success, 999));
        await db.SaveChangesAsync(Ct);
        return (connectionString, monitor.Id);
    }

    [Fact]
    public async Task Uptime_and_p95_are_calculated_per_window_from_the_checks_in_it()
    {
        var (connectionString, monitorId) = await SeedKnownDataAsync(postgres);
        await using var db = PostgresFixture.CreateContext(connectionString);

        var windows = await new MonitorMetricsQueries(db).GetUptimeAsync(monitorId, Now, Ct);

        Assert.Equal(["24h", "7d", "30d"], windows.Select(w => w.Window));
        var (day, week, month) = (windows[0], windows[1], windows[2]);

        Assert.Equal((3, 2), (day.TotalChecks, day.SuccessfulChecks));
        Assert.Equal(200d / 3, day.UptimePercent!.Value, precision: 6);
        Assert.Equal(195, day.P95DurationMs); // percentile_cont over [100, 200]; the 10 s timeout is excluded
        Assert.Equal(Now.AddHours(-24), day.FromUtc);
        Assert.Equal(Now, day.ToUtc);

        Assert.Equal((5, 3), (week.TotalChecks, week.SuccessfulChecks));
        Assert.Equal(60d, week.UptimePercent!.Value, precision: 6);
        Assert.Equal(290, week.P95DurationMs); // [100, 200, 300]

        Assert.Equal((6, 4), (month.TotalChecks, month.SuccessfulChecks));
        Assert.Equal(200d / 3, month.UptimePercent!.Value, precision: 6);
        Assert.Equal(385, month.P95DurationMs); // [100, 200, 300, 400]; the 31-day-old check is excluded
    }

    [Fact]
    public async Task An_empty_window_has_no_uptime_instead_of_100_percent()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var user = await TestData.AddUserAsync(db);
        var monitor = await TestData.AddMonitorAsync(db, user.Id);

        var windows = await new MonitorMetricsQueries(db).GetUptimeAsync(monitor.Id, Now, Ct);

        Assert.All(windows, w =>
        {
            Assert.Equal(0, w.TotalChecks);
            Assert.Null(w.UptimePercent);
            Assert.Null(w.P95DurationMs);
        });
    }

    [Fact]
    public async Task Only_failures_means_zero_uptime_and_no_p95()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var user = await TestData.AddUserAsync(db);
        var monitor = await TestData.AddMonitorAsync(db, user.Id);
        db.MonitorChecks.Add(Check(monitor.Id, TimeSpan.FromMinutes(10), CheckOutcome.ConnectionFailure, 30));
        await db.SaveChangesAsync(Ct);

        var day = (await new MonitorMetricsQueries(db).GetUptimeAsync(monitor.Id, Now, Ct))[0];

        Assert.Equal(0d, day.UptimePercent);
        Assert.Null(day.P95DurationMs);
    }

    [Theory]
    [InlineData("24h", 288, 300, 3)]
    [InlineData("7d", 168, 3600, 5)]
    [InlineData("30d", 180, 14400, 6)]
    public async Task The_series_has_every_aligned_bucket_and_counts_each_check_once(
        string windowName, int expectedPoints, int bucketSeconds, int expectedChecks)
    {
        var (connectionString, monitorId) = await SeedKnownDataAsync(postgres);
        await using var db = PostgresFixture.CreateContext(connectionString);

        var series = await new MonitorMetricsQueries(db).GetSeriesAsync(monitorId, StatsWindow.Parse(windowName)!, Now, Ct);

        Assert.Equal(expectedPoints, series.Points.Count);
        Assert.Equal(bucketSeconds, series.BucketSeconds);
        Assert.Equal(Now.AddSeconds(bucketSeconds), series.ToUtc);
        Assert.Equal(series.FromUtc, series.Points[0].BucketStartUtc);
        Assert.All(series.Points, p => Assert.Equal(0, p.BucketStartUtc.UtcTicks % TimeSpan.FromSeconds(bucketSeconds).Ticks));
        Assert.Equal(expectedChecks, series.Points.Sum(p => p.Checks));
    }

    [Fact]
    public async Task Series_buckets_average_successful_durations_and_count_failures_separately()
    {
        var (connectionString, monitorId) = await SeedKnownDataAsync(postgres);
        await using var db = PostgresFixture.CreateContext(connectionString);

        var points = (await new MonitorMetricsQueries(db).GetSeriesAsync(monitorId, StatsWindow.Day, Now, Ct)).Points;

        var oneHourAgo = Assert.Single(points, p => p.BucketStartUtc == Now.AddHours(-1));
        Assert.Equal((1, 0, 100, 100), (oneHourAgo.Checks, oneHourAgo.Failures, oneHourAgo.AvgDurationMs, oneHourAgo.MaxDurationMs));

        var timeout = Assert.Single(points, p => p.BucketStartUtc == Now.AddHours(-3));
        Assert.Equal((1, 1), (timeout.Checks, timeout.Failures));
        Assert.Null(timeout.AvgDurationMs); // a 10 s timeout must not look like slow success
        Assert.Null(timeout.MaxDurationMs);

        var empty = Assert.Single(points, p => p.BucketStartUtc == Now.AddHours(-5));
        Assert.Equal((0, 0), (empty.Checks, empty.Failures));
        Assert.Null(empty.AvgDurationMs);
    }

    [Fact]
    public async Task Stats_and_series_endpoints_are_owner_scoped_and_validate_the_window()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new ApiFactory(connectionString);
        using var owner = await factory.CreateSignedInClientAsync();
        using var other = await factory.CreateSignedInClientAsync();
        using var created = await owner.Http.PostAsJsonAsync("/api/v1/monitors", new { name = "API", url = "https://api.example.com/" }, Ct);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var stats = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/monitors/{id}/stats", Ct);
        var windows = stats.GetProperty("windows").EnumerateArray().ToList();
        Assert.Equal(3, windows.Count);
        Assert.All(windows, w =>
        {
            Assert.Equal(JsonValueKind.Null, w.GetProperty("uptimePercent").ValueKind);
            Assert.Equal(0, w.GetProperty("totalChecks").GetInt32());
        });

        var series = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/monitors/{id}/checks/series?window=7d", Ct);
        Assert.Equal(168, series.GetProperty("points").GetArrayLength());

        using var badWindow = await owner.Http.GetAsync($"/api/v1/monitors/{id}/checks/series?window=1y", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, badWindow.StatusCode);

        using var otherStats = await other.Http.GetAsync($"/api/v1/monitors/{id}/stats", Ct);
        using var otherSeries = await other.Http.GetAsync($"/api/v1/monitors/{id}/checks/series", Ct);
        Assert.Equal(HttpStatusCode.NotFound, otherStats.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherSeries.StatusCode);
    }

    [Fact]
    public async Task Dashboard_summary_counts_only_my_monitors_and_reports_stale_and_worker_status()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new ApiFactory(connectionString);
        using var me = await factory.CreateSignedInClientAsync();
        using var someoneElse = await factory.CreateSignedInClientAsync();

        async Task<Guid> CreateAsync(TestClient client, string name)
        {
            using var response = await client.Http.PostAsJsonAsync("/api/v1/monitors", new { name, url = "https://api.example.com/" }, Ct);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        }

        var fresh = await CreateAsync(me, "Fresh");
        var stale = await CreateAsync(me, "Stale");
        var paused = await CreateAsync(me, "Paused");
        var down = await CreateAsync(me, "Down");
        await CreateAsync(someoneElse, "Not mine");
        (await me.Http.PostAsync($"/api/v1/monitors/{paused}/pause", null, Ct)).Dispose();

        // Before any worker has run.
        var empty = await me.Http.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/summary", Ct);
        Assert.False(empty.GetProperty("workerOnline").GetBoolean());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("workerLastSeenAtUtc").ValueKind);

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            // "Stale": created long ago and never checked.
            await db.Database.ExecuteSqlAsync(
                $"UPDATE monitors SET created_at_utc = now() - interval '1 day' WHERE id = {stale}", Ct);

            // "Down": one failure with threshold 1 opens an incident.
            var monitor = await db.Monitors.SingleAsync(m => m.Id == down, Ct);
            monitor.UpdateDetails(monitor.Name, monitor.Url, monitor.Settings with { FailureThreshold = 1 }, false, DateTimeOffset.UtcNow);
            var failure = MonitorCheck.Create(down, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 5, CheckOutcome.Timeout, null, null);
            var evaluation = monitor.ApplyCheckResult(failure, openIncident: null);
            db.MonitorChecks.Add(failure);
            db.Incidents.Add(evaluation.Incident!);

            db.WorkerHeartbeats.Add(WorkerHeartbeat.Create("worker-1", "1.0.0", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
        }

        var summary = await me.Http.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/summary", Ct);

        Assert.Equal(4, summary.GetProperty("totalMonitors").GetInt32());
        Assert.Equal(2, summary.GetProperty("unknown").GetInt32()); // fresh + stale
        Assert.Equal(1, summary.GetProperty("paused").GetInt32());
        Assert.Equal(1, summary.GetProperty("down").GetInt32());
        Assert.Equal(0, summary.GetProperty("healthy").GetInt32());
        Assert.Equal(1, summary.GetProperty("stale").GetInt32()); // a paused monitor is never stale
        Assert.Equal(1, summary.GetProperty("openIncidents").GetInt32());
        Assert.True(summary.GetProperty("workerOnline").GetBoolean());
        Assert.NotEqual(Guid.Empty, fresh);

        var otherSummary = await someoneElse.Http.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/summary", Ct);
        Assert.Equal(1, otherSummary.GetProperty("totalMonitors").GetInt32());
        Assert.Equal(0, otherSummary.GetProperty("openIncidents").GetInt32());
    }
}

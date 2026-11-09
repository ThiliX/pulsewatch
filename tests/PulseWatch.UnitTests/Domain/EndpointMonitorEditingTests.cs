using PulseWatch.Domain.Common;
using PulseWatch.Domain.Monitoring;

namespace PulseWatch.UnitTests.Domain;

public class EndpointMonitorEditingTests
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    [Fact]
    public void Changing_the_url_resets_health_and_schedules_a_check_now()
    {
        var data = new MonitorTestData(failureThreshold: 3);
        data.Monitor.ApplyCheckResult(data.Failure(), null);
        var editAt = data.Now.AddSeconds(5);

        data.Monitor.UpdateDetails("Orders API", "https://api2.example.com/health", data.Monitor.Settings, hasOpenIncident: false, editAt);

        Assert.Equal("https://api2.example.com/health", data.Monitor.Url);
        Assert.Equal(MonitorState.Unknown, data.Monitor.State);
        Assert.Equal(0, data.Monitor.ConsecutiveFailures);
        Assert.Null(data.Monitor.StreakStartedAtUtc);
        Assert.Null(data.Monitor.LastCheckedAtUtc);
        Assert.Null(data.Monitor.LastOutcome);
        Assert.Equal(editAt, data.Monitor.NextCheckAtUtc);
    }

    [Fact]
    public void Changing_the_url_with_an_open_incident_keeps_the_monitor_down()
    {
        var data = new MonitorTestData(failureThreshold: 1);
        data.Monitor.ApplyCheckResult(data.Failure(), null);

        data.Monitor.UpdateDetails("Orders API", "https://api2.example.com/", data.Monitor.Settings, hasOpenIncident: true, data.Now);

        Assert.Equal(MonitorState.Down, data.Monitor.State);
    }

    [Fact]
    public void Renaming_keeps_health_and_schedule()
    {
        var data = new MonitorTestData();
        data.Monitor.ApplyCheckResult(data.Success(), null);
        var next = data.Monitor.NextCheckAtUtc;

        data.Monitor.UpdateDetails("Renamed", data.Monitor.Url, data.Monitor.Settings, hasOpenIncident: false, data.Now);

        Assert.Equal("Renamed", data.Monitor.Name);
        Assert.Equal(MonitorState.Healthy, data.Monitor.State);
        Assert.Equal(1, data.Monitor.ConsecutiveSuccesses);
        Assert.Equal(next, data.Monitor.NextCheckAtUtc);
    }

    [Fact]
    public void Changing_the_interval_reschedules_from_the_last_check()
    {
        var data = new MonitorTestData();
        var check = data.Success();
        data.Monitor.ApplyCheckResult(check, null);

        data.Monitor.UpdateDetails("Orders API", data.Monitor.Url, data.Monitor.Settings with { IntervalSeconds = 300 }, false, data.Now);

        Assert.Equal(check.CompletedAtUtc.AddSeconds(300), data.Monitor.NextCheckAtUtc);
    }

    [Fact]
    public void Invalid_edits_are_rejected_without_changing_anything()
    {
        var data = new MonitorTestData();

        Assert.Throws<DomainException>(() => data.Monitor.UpdateDetails(
            "Orders API", "ftp://example.com", data.Monitor.Settings, false, data.Now));
        Assert.Throws<DomainException>(() => data.Monitor.UpdateDetails(
            "Orders API", data.Monitor.Url, data.Monitor.Settings with { FailureThreshold = 0 }, false, data.Now));

        Assert.Equal("https://api.example.com/health", data.Monitor.Url);
        Assert.Equal(3, data.Monitor.FailureThreshold);
    }

    [Fact]
    public void Manual_check_makes_the_monitor_due_now_then_enforces_the_cooldown()
    {
        var data = new MonitorTestData();
        data.Monitor.ApplyCheckResult(data.Success(), null);
        var requestAt = data.Now.AddSeconds(1);

        Assert.True(data.Monitor.TryRequestManualCheck(requestAt, Cooldown, out _));
        Assert.Equal(requestAt, data.Monitor.ManualCheckRequestedAtUtc);
        Assert.True(data.Monitor.NextCheckAtUtc <= requestAt);

        Assert.False(data.Monitor.TryRequestManualCheck(requestAt.AddSeconds(20), Cooldown, out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(40), retryAfter);

        Assert.True(data.Monitor.TryRequestManualCheck(requestAt.AddSeconds(60), Cooldown, out _));
    }

    [Fact]
    public void Manual_check_on_a_paused_monitor_is_rejected()
    {
        var data = new MonitorTestData();
        data.Monitor.Pause(data.Now);

        Assert.Throws<DomainException>(() => data.Monitor.TryRequestManualCheck(data.Now, Cooldown, out _));
    }

    [Fact]
    public void Completing_a_check_schedules_one_interval_after_it_started()
    {
        var data = new MonitorTestData();
        var check = data.Success();
        data.Monitor.ApplyCheckResult(check, null);

        data.Monitor.CompleteCheck(check.StartedAtUtc, check.CompletedAtUtc);

        Assert.Equal(check.StartedAtUtc.AddSeconds(60), data.Monitor.NextCheckAtUtc);
        Assert.Null(data.Monitor.LeaseUntilUtc);
    }

    [Fact]
    public void Completing_a_check_never_schedules_in_the_past()
    {
        var data = new MonitorTestData();
        var started = data.Now;
        var muchLater = started.AddMinutes(10);

        data.Monitor.CompleteCheck(started, muchLater);

        Assert.Equal(muchLater, data.Monitor.NextCheckAtUtc);
    }

    [Fact]
    public void A_manual_request_during_a_check_keeps_the_monitor_due_now()
    {
        var data = new MonitorTestData();
        var started = data.Now;
        data.Monitor.TryRequestManualCheck(started.AddSeconds(2), TimeSpan.FromSeconds(60), out _);

        data.Monitor.CompleteCheck(started, started.AddSeconds(3));

        Assert.Equal(started.AddSeconds(3), data.Monitor.NextCheckAtUtc);
    }

    [Fact]
    public void Monitor_is_stale_after_two_intervals_plus_a_minute_without_results()
    {
        var data = new MonitorTestData(); // 60 s interval => stale after 180 s
        var check = data.Success();
        data.Monitor.ApplyCheckResult(check, null);

        Assert.False(data.Monitor.IsStale(check.CompletedAtUtc.AddSeconds(180)));
        Assert.True(data.Monitor.IsStale(check.CompletedAtUtc.AddSeconds(181)));
    }

    [Fact]
    public void Never_checked_monitor_becomes_stale_measured_from_creation_and_paused_is_never_stale()
    {
        var data = new MonitorTestData();

        Assert.False(data.Monitor.IsStale(MonitorTestData.Start.AddSeconds(100)));
        Assert.True(data.Monitor.IsStale(MonitorTestData.Start.AddMinutes(10)));

        data.Monitor.Pause(MonitorTestData.Start);
        Assert.False(data.Monitor.IsStale(MonitorTestData.Start.AddDays(1)));
    }
}

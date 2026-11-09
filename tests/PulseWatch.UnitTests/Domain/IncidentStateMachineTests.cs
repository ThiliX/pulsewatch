using PulseWatch.Domain.Common;
using PulseWatch.Domain.Incidents;
using PulseWatch.Domain.Monitoring;

namespace PulseWatch.UnitTests.Domain;

public class IncidentStateMachineTests
{
    [Fact]
    public void New_monitor_is_unknown_with_no_streaks()
    {
        var data = new MonitorTestData();

        Assert.Equal(MonitorState.Unknown, data.Monitor.State);
        Assert.Equal(0, data.Monitor.ConsecutiveFailures);
        Assert.Null(data.Monitor.LastCheckedAtUtc);
    }

    [Fact]
    public void Success_without_incident_is_healthy()
    {
        var data = new MonitorTestData();

        var result = data.Monitor.ApplyCheckResult(data.Success(), null);

        Assert.Equal(IncidentTransition.None, result.Transition);
        Assert.Null(result.Incident);
        Assert.Equal(MonitorState.Healthy, data.Monitor.State);
        Assert.Equal(1, data.Monitor.ConsecutiveSuccesses);
        Assert.Equal(200, data.Monitor.LastStatusCode);
    }

    [Fact]
    public void Failures_below_threshold_are_degraded_without_incident()
    {
        var data = new MonitorTestData(failureThreshold: 3);
        var first = data.Failure();

        data.Monitor.ApplyCheckResult(first, null);
        var result = data.Monitor.ApplyCheckResult(data.Failure(), null);

        Assert.Equal(IncidentTransition.None, result.Transition);
        Assert.Null(result.Incident);
        Assert.Equal(MonitorState.Degraded, data.Monitor.State);
        Assert.Equal(2, data.Monitor.ConsecutiveFailures);
        Assert.Equal(first.StartedAtUtc, data.Monitor.StreakStartedAtUtc);
    }

    [Fact]
    public void Incident_opens_exactly_at_the_threshold_with_first_failure_time()
    {
        var data = new MonitorTestData(failureThreshold: 3);
        var first = data.Failure(CheckOutcome.Timeout);
        data.Monitor.ApplyCheckResult(first, null);
        data.Monitor.ApplyCheckResult(data.Failure(CheckOutcome.Timeout), null);
        var third = data.Failure(CheckOutcome.Timeout);

        var result = data.Monitor.ApplyCheckResult(third, null);

        Assert.Equal(IncidentTransition.Opened, result.Transition);
        var incident = Assert.IsType<Incident>(result.Incident);
        Assert.True(incident.IsOpen);
        Assert.Equal(data.Monitor.Id, incident.MonitorId);
        Assert.Equal(first.StartedAtUtc, incident.FirstFailureAtUtc);
        Assert.Equal(third.CompletedAtUtc, incident.OpenedAtUtc);
        Assert.Equal(3, incident.FailureCount);
        Assert.Equal(CheckOutcome.Timeout, incident.OpeningOutcome);
        Assert.Null(incident.OpeningStatusCode);
        Assert.Equal(MonitorState.Down, data.Monitor.State);
    }

    [Fact]
    public void Threshold_of_one_opens_on_the_first_failure()
    {
        var data = new MonitorTestData(failureThreshold: 1);

        var result = data.Monitor.ApplyCheckResult(data.Failure(status: 500), null);

        Assert.Equal(IncidentTransition.Opened, result.Transition);
        Assert.Equal(500, result.Incident!.OpeningStatusCode);
    }

    [Fact]
    public void A_success_resets_the_failure_streak()
    {
        var data = new MonitorTestData(failureThreshold: 3);
        data.Monitor.ApplyCheckResult(data.Failure(), null);
        data.Monitor.ApplyCheckResult(data.Failure(), null);

        data.Monitor.ApplyCheckResult(data.Success(), null);
        var afterReset = data.Monitor.ApplyCheckResult(data.Failure(), null);

        Assert.Equal(IncidentTransition.None, afterReset.Transition);
        Assert.Equal(1, data.Monitor.ConsecutiveFailures);
        Assert.Equal(MonitorState.Degraded, data.Monitor.State);
    }

    [Fact]
    public void Failures_while_open_add_to_the_same_incident_and_never_open_another()
    {
        var data = new MonitorTestData(failureThreshold: 2);
        data.Monitor.ApplyCheckResult(data.Failure(), null);
        var incident = data.Monitor.ApplyCheckResult(data.Failure(), null).Incident!;

        for (var i = 0; i < 5; i++)
        {
            var check = data.Failure();
            var result = data.Monitor.ApplyCheckResult(check, incident);

            Assert.Equal(IncidentTransition.None, result.Transition);
            Assert.Same(incident, result.Incident);
            Assert.Equal(check.StartedAtUtc, incident.LastFailureAtUtc);
        }

        Assert.Equal(7, incident.FailureCount);
        Assert.True(incident.IsOpen);
        Assert.Equal(MonitorState.Down, data.Monitor.State);
    }

    [Fact]
    public void Recovery_threshold_of_one_resolves_on_the_first_success()
    {
        var data = new MonitorTestData(failureThreshold: 1, recoveryThreshold: 1);
        var incident = data.Monitor.ApplyCheckResult(data.Failure(), null).Incident!;
        var success = data.Success();

        var result = data.Monitor.ApplyCheckResult(success, incident);

        Assert.Equal(IncidentTransition.Resolved, result.Transition);
        Assert.Same(incident, result.Incident);
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(success.CompletedAtUtc, incident.ResolvedAtUtc);
        Assert.Equal(MonitorState.Healthy, data.Monitor.State);
    }

    [Fact]
    public void Recovery_threshold_of_two_needs_two_successes_in_a_row()
    {
        var data = new MonitorTestData(failureThreshold: 1, recoveryThreshold: 2);
        var incident = data.Monitor.ApplyCheckResult(data.Failure(), null).Incident!;

        var first = data.Monitor.ApplyCheckResult(data.Success(), incident);
        Assert.Equal(IncidentTransition.None, first.Transition);
        Assert.True(incident.IsOpen);
        Assert.Equal(MonitorState.Down, data.Monitor.State);

        var second = data.Monitor.ApplyCheckResult(data.Success(), incident);
        Assert.Equal(IncidentTransition.Resolved, second.Transition);
        Assert.False(incident.IsOpen);
    }

    [Fact]
    public void A_failure_between_recovery_successes_prevents_early_recovery()
    {
        var data = new MonitorTestData(failureThreshold: 1, recoveryThreshold: 2);
        var incident = data.Monitor.ApplyCheckResult(data.Failure(), null).Incident!;

        data.Monitor.ApplyCheckResult(data.Success(), incident);
        data.Monitor.ApplyCheckResult(data.Failure(), incident);
        var result = data.Monitor.ApplyCheckResult(data.Success(), incident);

        Assert.Equal(IncidentTransition.None, result.Transition);
        Assert.True(incident.IsOpen);
        Assert.Equal(1, data.Monitor.ConsecutiveSuccesses);
    }

    [Fact]
    public void Successes_after_recovery_do_not_resolve_again()
    {
        var data = new MonitorTestData(failureThreshold: 1);
        var incident = data.Monitor.ApplyCheckResult(data.Failure(), null).Incident!;
        data.Monitor.ApplyCheckResult(data.Success(), incident);

        // After recovery the caller has no open incident to pass in.
        var result = data.Monitor.ApplyCheckResult(data.Success(), null);

        Assert.Equal(IncidentTransition.None, result.Transition);
        Assert.Null(result.Incident);
    }

    [Fact]
    public void Passing_an_already_resolved_incident_is_rejected()
    {
        var data = new MonitorTestData(failureThreshold: 1);
        var incident = data.Monitor.ApplyCheckResult(data.Failure(), null).Incident!;
        data.Monitor.ApplyCheckResult(data.Success(), incident);

        Assert.Throws<DomainException>(() => data.Monitor.ApplyCheckResult(data.Success(), incident));
    }

    [Fact]
    public void A_check_for_another_monitor_is_rejected()
    {
        var data = new MonitorTestData();
        var other = new MonitorTestData();

        Assert.Throws<DomainException>(() => data.Monitor.ApplyCheckResult(other.Success(), null));
    }

    [Fact]
    public void An_incident_from_another_monitor_is_rejected()
    {
        var data = new MonitorTestData(failureThreshold: 1);
        var other = new MonitorTestData(failureThreshold: 1);
        var otherIncident = other.Monitor.ApplyCheckResult(other.Failure(), null).Incident!;

        Assert.Throws<DomainException>(() => data.Monitor.ApplyCheckResult(data.Failure(), otherIncident));
    }

    [Fact]
    public void Paused_monitor_records_the_result_but_changes_no_counters_or_incidents()
    {
        var data = new MonitorTestData(failureThreshold: 1);
        data.Monitor.Pause(data.Now);
        var check = data.Failure();

        var result = data.Monitor.ApplyCheckResult(check, null);

        Assert.Equal(IncidentTransition.None, result.Transition);
        Assert.Equal(MonitorState.Paused, data.Monitor.State);
        Assert.Equal(0, data.Monitor.ConsecutiveFailures);
        Assert.Equal(check.CompletedAtUtc, data.Monitor.LastCheckedAtUtc);
    }

    [Fact]
    public void Pausing_keeps_an_open_incident_open_and_resume_reports_down()
    {
        var data = new MonitorTestData(failureThreshold: 1);
        var incident = data.Monitor.ApplyCheckResult(data.Failure(), null).Incident!;

        data.Monitor.Pause(data.Now);
        Assert.True(incident.IsOpen);
        Assert.Equal(MonitorState.Paused, data.Monitor.State);

        var resumeAt = data.Now.AddMinutes(5);
        data.Monitor.Resume(resumeAt, hasOpenIncident: true);
        Assert.Equal(MonitorState.Down, data.Monitor.State);
        Assert.Equal(resumeAt, data.Monitor.NextCheckAtUtc);
    }

    [Fact]
    public void Resume_without_incident_is_unknown_until_the_next_result()
    {
        var data = new MonitorTestData();
        data.Monitor.ApplyCheckResult(data.Success(), null);
        data.Monitor.Pause(data.Now);

        data.Monitor.Resume(data.Now, hasOpenIncident: false);

        Assert.True(data.Monitor.IsEnabled);
        Assert.Equal(MonitorState.Unknown, data.Monitor.State);
    }

    [Fact]
    public void Pause_and_resume_are_idempotent()
    {
        var data = new MonitorTestData();
        data.Monitor.Pause(data.Now);
        var pausedAt = data.Monitor.UpdatedAtUtc;

        data.Monitor.Pause(data.Now.AddMinutes(1));
        Assert.Equal(pausedAt, data.Monitor.UpdatedAtUtc);

        data.Monitor.Resume(data.Now.AddMinutes(2), hasOpenIncident: false);
        var resumedNext = data.Monitor.NextCheckAtUtc;
        data.Monitor.Resume(data.Now.AddMinutes(3), hasOpenIncident: false);
        Assert.Equal(resumedNext, data.Monitor.NextCheckAtUtc);
    }

    [Fact]
    public void Incident_duration_is_measured_from_the_first_failure()
    {
        var data = new MonitorTestData(failureThreshold: 2);
        var first = data.Failure();
        data.Monitor.ApplyCheckResult(first, null);
        var incident = data.Monitor.ApplyCheckResult(data.Failure(), null).Incident!;

        var openDuration = incident.GetDuration(first.StartedAtUtc.AddMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(10), openDuration);

        var success = data.Success();
        data.Monitor.ApplyCheckResult(success, incident);
        Assert.Equal(success.CompletedAtUtc - first.StartedAtUtc, incident.GetDuration(DateTimeOffset.MaxValue));
    }
}

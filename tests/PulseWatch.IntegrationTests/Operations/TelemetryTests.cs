using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseWatch.Application.Probing;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Notifications;
using PulseWatch.Infrastructure.Telemetry;
using PulseWatch.IntegrationTests.Infrastructure;
using PulseWatch.Worker;

namespace PulseWatch.IntegrationTests.Operations;

/// <summary>The worker's metrics and log scopes, observed on a real worker host with a scripted probe.</summary>
public class TelemetryTests(PostgresFixture postgres)
{
    private const string SecretPath = "/v1/accounts/acct-9f3k2-secret";
    private const string SecretQuery = "api_key=TOP-SECRET-KEY";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class DownProbe : IProbeRunner
    {
        public Task<ProbeResult> ProbeAsync(ProbeRequest request, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ProbeResult(now, now.AddMilliseconds(42), 42, CheckOutcome.HttpStatusFailure, 503, "503"));
        }
    }

    /// <summary>Records measurements from one meter instance only, so parallel tests don't interfere.</summary>
    private sealed class MeterCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        public MeterCapture(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Record(i, v, tags));
            _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Record(i, v, tags));
            _listener.Start();
        }

        public ConcurrentQueue<(string Name, double Value, string Tags)> Measurements { get; } = new();

        public void Observe() => _listener.RecordObservableInstruments();

        public double Sum(string name) => Measurements.Where(m => m.Name == name).Sum(m => m.Value);

        public void Dispose() => _listener.Dispose();

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
            Measurements.Enqueue((instrument.Name, value, string.Join(",", tags.ToArray().Select(t => $"{t.Key}={t.Value}"))));
    }

    [Fact]
    public async Task Worker_records_metrics_and_logs_scopes_with_the_url_reduced_to_scheme_and_host()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        Guid monitorId;
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var user = await TestData.AddUserAsync(db);
            var monitor = EndpointMonitor.Create(user.Id, "Accounts API", $"https://api.example.com{SecretPath}?{SecretQuery}",
                MonitorSettings.Default with { FailureThreshold = 1 }, DateTimeOffset.UtcNow.AddMinutes(-2));
            db.Monitors.Add(monitor);
            await db.SaveChangesAsync(Ct);
            monitorId = monitor.Id;
        }

        var logs = new CapturingLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddProvider(logs);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PulseWatch"] = connectionString,
            ["Monitoring:PollIntervalSeconds"] = "1",
            ["Notifications:PollIntervalSeconds"] = "1",
            ["Retention:StartDelaySeconds"] = "3600",
        });
        builder.Services.AddPulseWatchWorker(builder.Configuration);
        builder.Services.RemoveAll<IProbeRunner>();
        builder.Services.AddSingleton<IProbeRunner, DownProbe>();

        using var worker = builder.Build();
        using var capture = new MeterCapture(worker.Services.GetRequiredService<PulseWatchMetrics>().Meter);
        await worker.StartAsync(Ct);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (true)
            {
                await using var db = PostgresFixture.CreateContext(connectionString);
                if (await db.NotificationOutbox.AnyAsync(m => m.Status == NotificationStatus.Sent, Ct))
                {
                    break;
                }

                Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the alert email to be sent.");
                await Task.Delay(200, Ct);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        capture.Observe();

        // Metrics
        Assert.Contains(capture.Measurements, m =>
            m.Name == "pulsewatch.checks.completed" && m.Value == 1 && m.Tags == "outcome=HttpStatusFailure");
        Assert.Contains(capture.Measurements, m => m.Name == "pulsewatch.check.duration" && m.Value == 42);
        var lag = Assert.Single(capture.Measurements, m => m.Name == "pulsewatch.worker.lag");
        Assert.InRange(lag.Value, 100, 200); // due two minutes before the worker started
        Assert.Equal(1, capture.Sum("pulsewatch.incidents.opened"));
        Assert.Equal(1, capture.Sum("pulsewatch.notifications.sent"));
        Assert.Contains(capture.Measurements, m => m.Name == "pulsewatch.outbox.pending");

        // Log scopes: the check's lines carry the monitor id, the check id, a trace id and only the URL's origin.
        var checkedLine = Assert.Single(logs.Lines, l => l.Contains("Checked monitor", StringComparison.Ordinal));
        Assert.Contains($"MonitorId={monitorId}", checkedLine, StringComparison.Ordinal);
        Assert.Contains("Target=https://api.example.com ", checkedLine, StringComparison.Ordinal);
        Assert.Matches("CheckId=[0-9a-f-]{36}", checkedLine);
        Assert.Matches("TraceId=[0-9a-f]{32}", checkedLine);
        Assert.Contains(logs.Lines, l => l.Contains("Incident Opened", StringComparison.Ordinal));
        Assert.Contains(logs.Lines, l => l.Contains("Sent notification", StringComparison.Ordinal) && l.Contains("MessageId=", StringComparison.Ordinal));

        Assert.DoesNotContain("acct-9f3k2-secret", logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain("TOP-SECRET-KEY", logs.AllText, StringComparison.Ordinal);
    }
}

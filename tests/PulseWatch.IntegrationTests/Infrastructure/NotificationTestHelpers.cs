using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PulseWatch.Application.Notifications;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Notifications;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.IntegrationTests.Infrastructure;

/// <summary>A clock the test moves by hand. Only GetUtcNow is overridden; timers still use real time.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public DateTimeOffset Now => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}

/// <summary>
/// A fake sender that fails according to a script: <see cref="FailuresBeforeSuccess"/> transient failures,
/// then success; or always fail with <see cref="AlwaysFail"/>.
/// </summary>
internal sealed class ScriptedEmailSender : IEmailSender
{
    private int _calls;

    public int FailuresBeforeSuccess { get; set; }

    public EmailDeliveryException? AlwaysFail { get; set; }

    public int Calls => Volatile.Read(ref _calls);

    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _calls);
        if (AlwaysFail is not null)
        {
            throw AlwaysFail;
        }

        if (call <= FailuresBeforeSuccess)
        {
            throw new EmailDeliveryException(EmailErrorCategories.ConnectionFailed, permanent: false);
        }

        Sent.Enqueue(message);
        return Task.CompletedTask;
    }
}

/// <summary>Collects every log line (with exception details) so tests can assert on what was logged.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public string AllText => string.Join('\n', _lines);

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
    }
}

internal static class NotificationTestData
{
    /// <summary>
    /// A user, a monitor with an open incident (opened through the real state machine) and one pending
    /// IncidentOpened outbox message due at <paramref name="now"/>.
    /// </summary>
    public static async Task<(Guid MessageId, Guid IncidentId, string Recipient)> AddPendingMessageAsync(
        PulseWatchDbContext db, DateTimeOffset now, string? monitorName = null)
    {
        var user = await TestData.AddUserAsync(db);
        var monitor = EndpointMonitor.Create(
            user.Id,
            monitorName ?? "Orders API",
            "https://api.example.com/health?key=abc",
            MonitorSettings.Default with { FailureThreshold = 1 },
            now.AddMinutes(-5));
        db.Monitors.Add(monitor);

        var failure = MonitorCheck.Create(monitor.Id, now.AddMinutes(-1), now.AddMinutes(-1).AddMilliseconds(50), 50,
            CheckOutcome.HttpStatusFailure, 503, null);
        var evaluation = monitor.ApplyCheckResult(failure, openIncident: null);
        var incident = evaluation.Incident ?? throw new InvalidOperationException("Expected an incident to open.");
        db.MonitorChecks.Add(failure);
        db.Incidents.Add(incident);

        var message = NotificationOutboxMessage.Create(
            incident.Id, NotificationEventType.IncidentOpened, NotificationChannel.Email, user.Email!, now);
        db.NotificationOutbox.Add(message);
        await db.SaveChangesAsync();
        return (message.Id, incident.Id, user.Email!);
    }

    public static async Task<NotificationOutboxMessage> GetAsync(string connectionString, Guid messageId)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        return await db.NotificationOutbox.AsNoTracking().SingleAsync(m => m.Id == messageId);
    }
}

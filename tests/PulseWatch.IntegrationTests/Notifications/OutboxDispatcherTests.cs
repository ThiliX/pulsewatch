using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseWatch.Application.Notifications;
using PulseWatch.Domain.Notifications;
using PulseWatch.Infrastructure.Notifications;
using PulseWatch.IntegrationTests.Infrastructure;
using PulseWatch.Worker;

namespace PulseWatch.IntegrationTests.Notifications;

/// <summary>
/// The real dispatcher and outbox store against PostgreSQL, with a scripted email sender and a manual
/// clock. Each test drives one batch at a time instead of starting the background loop.
/// </summary>
public class OutboxDispatcherTests(PostgresFixture postgres)
{
    private static readonly TimeSpan PastAnyBackoff = TimeSpan.FromHours(2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IHost BuildWorker(string connectionString, IEmailSender sender, ManualTimeProvider time)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PulseWatch"] = connectionString,
            ["App:PublicBaseUrl"] = "https://pulse.example.com",
        });
        builder.Services.AddPulseWatchWorker(builder.Configuration);
        builder.Services.RemoveAll<IEmailSender>();
        builder.Services.AddSingleton(sender);
        builder.Services.RemoveAll<TimeProvider>();
        builder.Services.AddSingleton<TimeProvider>(time);
        return builder.Build();
    }

    private static OutboxDispatcherService Dispatcher(IHost host) =>
        host.Services.GetServices<IHostedService>().OfType<OutboxDispatcherService>().Single();

    private async Task<(string ConnectionString, Guid MessageId, Guid IncidentId, string Recipient)> SeedAsync(DateTimeOffset now)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var (messageId, incidentId, recipient) = await NotificationTestData.AddPendingMessageAsync(db, now);
        return (connectionString, messageId, incidentId, recipient);
    }

    [Fact]
    public async Task Sends_a_pending_message_once_with_safe_content_and_marks_it_sent()
    {
        var time = new ManualTimeProvider(TestData.Now);
        var (connectionString, messageId, incidentId, recipient) = await SeedAsync(time.Now);
        var sender = new ScriptedEmailSender();
        using var host = BuildWorker(connectionString, sender, time);

        Assert.Equal(1, await Dispatcher(host).DispatchBatchAsync(Ct));
        Assert.Equal(0, await Dispatcher(host).DispatchBatchAsync(Ct));

        var email = Assert.Single(sender.Sent);
        Assert.Equal(recipient, email.To);
        Assert.Equal("[PulseWatch] DOWN: Orders API", email.Subject);
        Assert.Contains($"https://pulse.example.com/incidents/{incidentId:D}", email.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("key=abc", email.TextBody, StringComparison.Ordinal);

        var message = await NotificationTestData.GetAsync(connectionString, messageId);
        Assert.Equal(NotificationStatus.Sent, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal(time.Now, message.SentAtUtc);
        Assert.Null(message.LeaseUntilUtc);
    }

    [Fact]
    public async Task Fails_twice_then_works_ends_sent_on_attempt_three_respecting_backoff()
    {
        var time = new ManualTimeProvider(TestData.Now);
        var (connectionString, messageId, _, _) = await SeedAsync(time.Now);
        var sender = new ScriptedEmailSender { FailuresBeforeSuccess = 2 };
        using var host = BuildWorker(connectionString, sender, time);
        var dispatcher = Dispatcher(host);

        // Attempt 1 fails: Failed, retry scheduled 24–36 s later (30 s ± 20 %).
        Assert.Equal(1, await dispatcher.DispatchBatchAsync(Ct));
        var afterFirst = await NotificationTestData.GetAsync(connectionString, messageId);
        Assert.Equal(NotificationStatus.Failed, afterFirst.Status);
        Assert.Equal(1, afterFirst.AttemptCount);
        Assert.Equal(EmailErrorCategories.ConnectionFailed, afterFirst.LastErrorCategory);
        var delay = afterFirst.NextAttemptAtUtc - time.Now;
        Assert.InRange(delay, TimeSpan.FromSeconds(24), TimeSpan.FromSeconds(36));

        // Not retried before the backoff has passed.
        time.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(0, await dispatcher.DispatchBatchAsync(Ct));

        // Attempt 2 fails, attempt 3 succeeds.
        time.Advance(PastAnyBackoff);
        Assert.Equal(1, await dispatcher.DispatchBatchAsync(Ct));
        time.Advance(PastAnyBackoff);
        Assert.Equal(1, await dispatcher.DispatchBatchAsync(Ct));

        var message = await NotificationTestData.GetAsync(connectionString, messageId);
        Assert.Equal(NotificationStatus.Sent, message.Status);
        Assert.Equal(3, message.AttemptCount);
        Assert.Null(message.LastErrorCategory);
        Assert.Equal(3, sender.Calls);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task Always_failing_becomes_exhausted_after_max_attempts_and_is_never_sent_again()
    {
        var time = new ManualTimeProvider(TestData.Now);
        var (connectionString, messageId, _, _) = await SeedAsync(time.Now);
        var sender = new ScriptedEmailSender
        {
            AlwaysFail = new EmailDeliveryException(EmailErrorCategories.Timeout, permanent: false),
        };
        using var host = BuildWorker(connectionString, sender, time);
        var dispatcher = Dispatcher(host);

        for (var attempt = 1; attempt <= 6; attempt++)
        {
            Assert.Equal(1, await dispatcher.DispatchBatchAsync(Ct));
            time.Advance(PastAnyBackoff);
        }

        time.Advance(TimeSpan.FromDays(7));
        Assert.Equal(0, await dispatcher.DispatchBatchAsync(Ct));

        var message = await NotificationTestData.GetAsync(connectionString, messageId);
        Assert.Equal(NotificationStatus.Exhausted, message.Status);
        Assert.Equal(6, message.AttemptCount);
        Assert.Equal(EmailErrorCategories.Timeout, message.LastErrorCategory);
        Assert.Equal(6, sender.Calls);
    }

    [Fact]
    public async Task A_permanent_failure_is_exhausted_immediately()
    {
        var time = new ManualTimeProvider(TestData.Now);
        var (connectionString, messageId, _, _) = await SeedAsync(time.Now);
        var sender = new ScriptedEmailSender
        {
            AlwaysFail = new EmailDeliveryException(EmailErrorCategories.RecipientRejected, permanent: true),
        };
        using var host = BuildWorker(connectionString, sender, time);

        Assert.Equal(1, await Dispatcher(host).DispatchBatchAsync(Ct));
        time.Advance(PastAnyBackoff);
        Assert.Equal(0, await Dispatcher(host).DispatchBatchAsync(Ct));

        var message = await NotificationTestData.GetAsync(connectionString, messageId);
        Assert.Equal(NotificationStatus.Exhausted, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal(EmailErrorCategories.RecipientRejected, message.LastErrorCategory);
    }

    [Fact]
    public async Task A_restarted_dispatcher_does_not_send_a_sent_message_again()
    {
        var time = new ManualTimeProvider(TestData.Now);
        var (connectionString, messageId, _, _) = await SeedAsync(time.Now);
        var sender = new ScriptedEmailSender();

        using (var first = BuildWorker(connectionString, sender, time))
        {
            Assert.Equal(1, await Dispatcher(first).DispatchBatchAsync(Ct));
        }

        time.Advance(TimeSpan.FromDays(1));
        using (var restarted = BuildWorker(connectionString, sender, time))
        {
            Assert.Equal(0, await Dispatcher(restarted).DispatchBatchAsync(Ct));
        }

        Assert.Equal(1, sender.Calls);
        Assert.Equal(NotificationStatus.Sent, (await NotificationTestData.GetAsync(connectionString, messageId)).Status);
    }

    [Fact]
    public async Task A_message_left_sending_by_a_crashed_worker_is_sent_after_its_lease_expires()
    {
        var time = new ManualTimeProvider(TestData.Now);
        var (connectionString, messageId, _, _) = await SeedAsync(time.Now);
        var lease = TimeSpan.FromMinutes(3);

        // A worker claims the message and dies before sending or recording anything.
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Single(await new OutboxStore(db).ClaimDueAsync(time.Now, 10, lease, maxAttempts: 6, Ct));
        }

        var sender = new ScriptedEmailSender();
        using var host = BuildWorker(connectionString, sender, time);

        // Still leased: nobody else touches it.
        Assert.Equal(0, await Dispatcher(host).DispatchBatchAsync(Ct));

        time.Advance(lease + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await Dispatcher(host).DispatchBatchAsync(Ct));

        var message = await NotificationTestData.GetAsync(connectionString, messageId);
        Assert.Equal(NotificationStatus.Sent, message.Status);
        Assert.Equal(2, message.AttemptCount);
        Assert.Equal(1, sender.Calls);
    }

    [Fact]
    public async Task A_message_interrupted_on_its_last_attempt_is_exhausted_instead_of_sent_again()
    {
        var time = new ManualTimeProvider(TestData.Now);
        var (connectionString, messageId, _, _) = await SeedAsync(time.Now);
        var lease = TimeSpan.FromMinutes(3);

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            await db.Database.ExecuteSqlAsync(
                $"UPDATE notification_outbox SET status = {(short)NotificationStatus.Failed}, attempt_count = 5 WHERE id = {messageId}", Ct);
            Assert.Single(await new OutboxStore(db).ClaimDueAsync(time.Now, 10, lease, maxAttempts: 6, Ct)); // attempt 6, then "crash"
        }

        var sender = new ScriptedEmailSender();
        using var host = BuildWorker(connectionString, sender, time);
        time.Advance(lease + TimeSpan.FromSeconds(1));
        Assert.Equal(0, await Dispatcher(host).DispatchBatchAsync(Ct));

        var message = await NotificationTestData.GetAsync(connectionString, messageId);
        Assert.Equal(NotificationStatus.Exhausted, message.Status);
        Assert.Equal(EmailErrorCategories.Interrupted, message.LastErrorCategory);
        Assert.Equal(0, sender.Calls);
    }

    [Fact]
    public async Task A_late_result_from_an_expired_lease_does_not_overwrite_the_newer_attempt()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var now = TestData.Now;
        var (messageId, _, _) = await NotificationTestData.AddPendingMessageAsync(db, now);
        var lease = TimeSpan.FromMinutes(3);

        var slow = Assert.Single(await new OutboxStore(db).ClaimDueAsync(now, 10, lease, 6, Ct));
        var takeover = Assert.Single(await new OutboxStore(db).ClaimDueAsync(now + lease + TimeSpan.FromSeconds(1), 10, lease, 6, Ct));

        await using (var other = PostgresFixture.CreateContext(connectionString))
        {
            Assert.False(await new OutboxStore(other).MarkFailedAsync(slow, now, "Timeout", false, 6, TimeSpan.FromSeconds(30), Ct));
        }

        await using (var other = PostgresFixture.CreateContext(connectionString))
        {
            Assert.True(await new OutboxStore(other).MarkSentAsync(takeover, now + lease, Ct));
        }

        var message = await NotificationTestData.GetAsync(connectionString, messageId);
        Assert.Equal(NotificationStatus.Sent, message.Status);
        Assert.Equal(2, message.AttemptCount);
    }

    [Fact]
    public async Task Two_dispatchers_running_at_once_send_each_message_exactly_once()
    {
        var time = new ManualTimeProvider(TestData.Now);
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            for (var i = 0; i < 30; i++)
            {
                await NotificationTestData.AddPendingMessageAsync(db, time.Now, $"API {i}");
            }
        }

        var sender = new ScriptedEmailSender();
        using var a = BuildWorker(connectionString, sender, time);
        using var b = BuildWorker(connectionString, sender, time);

        await Task.WhenAll(
            Dispatcher(a).PollAsync(Ct),
            Dispatcher(b).PollAsync(Ct),
            Dispatcher(a).PollAsync(Ct),
            Dispatcher(b).PollAsync(Ct));

        Assert.Equal(30, sender.Calls);
        Assert.Equal(30, sender.Sent.Select(m => m.Subject).Distinct(StringComparer.Ordinal).Count());
        await using var read = PostgresFixture.CreateContext(connectionString);
        Assert.Equal(30, await read.NotificationOutbox.CountAsync(m => m.Status == NotificationStatus.Sent && m.AttemptCount == 1, Ct));
    }
}

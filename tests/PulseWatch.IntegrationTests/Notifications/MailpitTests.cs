using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseWatch.Application.Notifications;
using PulseWatch.Domain.Notifications;
using PulseWatch.IntegrationTests.Infrastructure;
using PulseWatch.Worker;

namespace PulseWatch.IntegrationTests.Notifications;

/// <summary>A disposable Mailpit (same image as docker-compose.yml) that requires SMTP authentication.</summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    public const string Username = "pulse";
    public const string Password = "Mailpit-Correct-Pa55!";

    private readonly IContainer _container = new ContainerBuilder("axllent/mailpit:v1.33.1")
        .WithEnvironment("MP_SMTP_AUTH", $"{Username}:{Password}")
        .WithEnvironment("MP_SMTP_AUTH_ALLOW_INSECURE", "true")
        .WithPortBinding(1025, assignRandomHostPort: true)
        .WithPortBinding(8025, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/readyz")))
        .Build();

    public string Host => _container.Hostname;

    public int SmtpPort => _container.GetMappedPublicPort(1025);

    public HttpClient Api { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        Api.BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(8025)}/");
    }

    public async ValueTask DisposeAsync()
    {
        Api.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>Messages addressed to <paramref name="recipient"/> (Mailpit search API).</summary>
    public async Task<JsonElement[]> MessagesToAsync(string recipient, CancellationToken cancellationToken)
    {
        var result = await Api.GetFromJsonAsync<JsonElement>(
            $"api/v1/search?query={Uri.EscapeDataString($"to:\"{recipient}\"")}", cancellationToken);
        return [.. result.GetProperty("messages").EnumerateArray()];
    }
}

public class MailpitTests(PostgresFixture postgres, MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IHost BuildWorker(string connectionString, string password, CapturingLoggerProvider logs)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(logs);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PulseWatch"] = connectionString,
            ["App:PublicBaseUrl"] = "https://pulse.example.com",
            ["Notifications:Provider"] = "Smtp",
            ["Email:Host"] = mailpit.Host,
            ["Email:Port"] = mailpit.SmtpPort.ToString(CultureInfo.InvariantCulture),
            ["Email:UseStartTls"] = "false",
            ["Email:Username"] = MailpitFixture.Username,
            ["Email:Password"] = password,
            ["Email:FromAddress"] = "alerts@pulsewatch.test",
        });
        builder.Services.AddPulseWatchWorker(builder.Configuration);
        return builder.Build();
    }

    private static OutboxDispatcherService Dispatcher(IHost host) =>
        host.Services.GetServices<IHostedService>().OfType<OutboxDispatcherService>().Single();

    [Fact]
    public async Task The_smtp_sender_delivers_the_alert_with_text_and_html_bodies()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var (messageId, incidentId, recipient) = await NotificationTestData.AddPendingMessageAsync(
            db, DateTimeOffset.UtcNow, "<b>Orders</b> API");
        var logs = new CapturingLoggerProvider();
        using var worker = BuildWorker(connectionString, MailpitFixture.Password, logs);

        Assert.Equal(1, await Dispatcher(worker).DispatchBatchAsync(Ct));

        var stored = await NotificationTestData.GetAsync(connectionString, messageId);
        Assert.Equal(NotificationStatus.Sent, stored.Status);

        var summary = Assert.Single(await mailpit.MessagesToAsync(recipient, Ct));
        Assert.Equal("[PulseWatch] DOWN: <b>Orders</b> API", summary.GetProperty("Subject").GetString());
        var full = await mailpit.Api.GetFromJsonAsync<JsonElement>($"api/v1/message/{summary.GetProperty("ID").GetString()}", Ct);
        var text = full.GetProperty("Text").GetString()!;
        var html = full.GetProperty("HTML").GetString()!;
        Assert.Contains($"https://pulse.example.com/incidents/{incidentId:D}", text, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;Orders&lt;/b&gt; API", html, StringComparison.Ordinal);
        Assert.DoesNotContain("key=abc", text + html, StringComparison.Ordinal);
        Assert.Equal("alerts@pulsewatch.test", full.GetProperty("From").GetProperty("Address").GetString());

        Assert.DoesNotContain(MailpitFixture.Password, logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_smtp_password_is_retried_later_and_never_logged()
    {
        const string WrongPassword = "Wrong-Pa55word-Must-Not-Leak!";
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        var (messageId, _, recipient) = await NotificationTestData.AddPendingMessageAsync(db, DateTimeOffset.UtcNow);
        var logs = new CapturingLoggerProvider();
        using var worker = BuildWorker(connectionString, WrongPassword, logs);

        Assert.Equal(1, await Dispatcher(worker).DispatchBatchAsync(Ct));

        var stored = await NotificationTestData.GetAsync(connectionString, messageId);
        Assert.Equal(NotificationStatus.Failed, stored.Status);
        Assert.Equal(EmailErrorCategories.AuthenticationFailed, stored.LastErrorCategory);
        Assert.Empty(await mailpit.MessagesToAsync(recipient, Ct));

        Assert.Contains(logs.Lines, l => l.Contains("AuthenticationFailed", StringComparison.Ordinal));
        Assert.DoesNotContain(WrongPassword, logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(MailpitFixture.Password, logs.AllText, StringComparison.Ordinal);
    }
}

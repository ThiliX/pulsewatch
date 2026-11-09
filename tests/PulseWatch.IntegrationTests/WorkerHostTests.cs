using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseWatch.Worker;

namespace PulseWatch.IntegrationTests;

public class WorkerHostTests
{
    [Fact]
    public async Task Heartbeat_service_starts_and_stops_cleanly()
    {
        using var host = BuildHost(new Dictionary<string, string?>());

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Single(host.Services.GetServices<IHostedService>().OfType<HeartbeatService>());
    }

    [Fact]
    public async Task Invalid_heartbeat_setting_fails_at_startup()
    {
        using var host = BuildHost(new Dictionary<string, string?> { ["Worker:HeartbeatSeconds"] = "1" });

        await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));
    }

    private static IHost BuildHost(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services
            .AddOptions<HeartbeatOptions>()
            .Bind(builder.Configuration.GetSection(HeartbeatOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddHostedService<HeartbeatService>();
        return builder.Build();
    }
}

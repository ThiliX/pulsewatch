using PulseWatch.Application.Options;
using PulseWatch.Infrastructure.Network;
using PulseWatch.Infrastructure.Notifications;
using PulseWatch.Infrastructure.Persistence;
using PulseWatch.Infrastructure.Probing;
using PulseWatch.Infrastructure.Scheduling;

namespace PulseWatch.Worker;

public static class WorkerSetup
{
    /// <summary>Everything the worker host needs. Shared with the integration tests.</summary>
    public static IServiceCollection AddPulseWatchWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<MonitoringOptions>()
            .Bind(configuration.GetSection(MonitoringOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<HeartbeatOptions>()
            .Bind(configuration.GetSection(HeartbeatOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection(AppOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var provider = configuration.GetSection(NotificationOptions.SectionName)
            .GetValue(nameof(NotificationOptions.Provider), EmailProvider.Logging);
        var email = services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations();
        if (provider == EmailProvider.Smtp)
        {
            email.Validate(
                o => !string.IsNullOrWhiteSpace(o.Host) && !string.IsNullOrWhiteSpace(o.FromAddress),
                "Email:Host and Email:FromAddress are required when Notifications:Provider is Smtp.");
        }

        email.ValidateOnStart();

        services.AddPulseWatchPersistence(configuration);
        services.AddPulseWatchNetwork();
        services.AddPulseWatchProbing();
        services.AddPulseWatchScheduling();
        services.AddPulseWatchNotifications(provider);

        services.AddHostedService<HeartbeatService>();
        services.AddHostedService<SchedulerService>();
        services.AddHostedService<OutboxDispatcherService>();

        // In-flight checks get time to finish and be recorded (probe timeout is at most 15 s).
        services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
        return services;
    }
}

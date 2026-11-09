using PulseWatch.Application.Options;
using PulseWatch.Infrastructure.Network;
using PulseWatch.Infrastructure.Persistence;
using PulseWatch.Infrastructure.Probing;
using PulseWatch.Infrastructure.Scheduling;
using PulseWatch.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPulseWatchWorker(builder.Configuration);

var host = builder.Build();
host.Run();

namespace PulseWatch.Worker
{
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

            services.AddPulseWatchPersistence(configuration);
            services.AddPulseWatchNetwork();
            services.AddPulseWatchProbing();
            services.AddPulseWatchScheduling();

            services.AddHostedService<HeartbeatService>();
            services.AddHostedService<SchedulerService>();

            // In-flight checks get time to finish and be recorded (probe timeout is at most 15 s).
            services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
            return services;
        }
    }
}

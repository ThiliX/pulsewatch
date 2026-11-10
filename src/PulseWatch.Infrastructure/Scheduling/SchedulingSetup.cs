using Microsoft.Extensions.DependencyInjection;
using PulseWatch.Application.Scheduling;

namespace PulseWatch.Infrastructure.Scheduling;

public static class SchedulingSetup
{
    /// <summary>Registers monitor claiming, result recording and the heartbeat store (all scoped: one DbContext each).</summary>
    public static IServiceCollection AddPulseWatchScheduling(this IServiceCollection services)
    {
        services.AddScoped<IMonitorClaimer, MonitorClaimer>();
        services.AddScoped<ICheckResultRecorder, CheckResultRecorder>();
        services.AddScoped<IWorkerHeartbeatStore, WorkerHeartbeatStore>();
        services.AddScoped<ICheckRetentionStore, CheckRetentionStore>();
        return services;
    }
}

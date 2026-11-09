using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PulseWatch.Application.Options;
using PulseWatch.Application.Probing;
using PulseWatch.Application.Security;

namespace PulseWatch.Infrastructure.Probing;

public static class ProbingSetup
{
    /// <summary>
    /// Registers the probe HttpClient on top of <see cref="SafeConnect"/>. Deliberately no retry/resilience
    /// handler (one attempt per check) and no default HTTP logging (it would log full URLs with query strings).
    /// </summary>
    public static IServiceCollection AddPulseWatchProbing(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAddressPolicy>(PublicOnlyAddressPolicy.Instance);
        services.TryAddSingleton<IDestinationValidator>(sp => new DestinationValidator(
            sp.GetRequiredService<IDnsResolver>(),
            sp.GetRequiredService<IOptions<MonitoringOptions>>().Value.AllowedPorts,
            sp.GetRequiredService<IAddressPolicy>()));

        services
            .AddHttpClient<IProbeRunner, HttpProbeRunner>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(sp => SafeConnect.CreateHandler(
                sp.GetRequiredService<IDnsResolver>(),
                sp.GetRequiredService<IAddressPolicy>()))
            .RemoveAllLoggers();

        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseWatch.Application.Security;

namespace PulseWatch.Infrastructure.Network;

public static class NetworkSetup
{
    /// <summary>Registers the system DNS resolver used by destination (SSRF) checks.</summary>
    public static IServiceCollection AddPulseWatchNetwork(this IServiceCollection services)
    {
        services.TryAddSingleton<IDnsResolver, SystemDnsResolver>();
        return services;
    }
}

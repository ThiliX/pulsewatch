using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using IPNetwork = System.Net.IPNetwork;

namespace PulseWatch.Api.Hosting;

/// <summary>
/// Section "ReverseProxy". When PulseWatch runs behind a proxy (Caddy in docker-compose.prod.yml), the proxy's
/// X-Forwarded-For and X-Forwarded-Proto headers are trusted, but only from these networks. Without them every
/// request would seem to come from the proxy: per-IP rate limits would be shared by all users, and HSTS and
/// Secure cookies would see plain http.
/// </summary>
public sealed class ReverseProxyOptions : IValidatableObject
{
    public const string SectionName = "ReverseProxy";

    /// <summary>CIDR ranges the proxy connects from, e.g. "172.30.10.0/24". Empty = not behind a proxy.</summary>
    public IList<string> KnownNetworks { get; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var network in KnownNetworks)
        {
            if (!IPNetwork.TryParse(network, out _))
            {
                yield return new ValidationResult(
                    $"ReverseProxy:KnownNetworks contains '{network}', which is not a CIDR range such as 172.30.10.0/24.",
                    [nameof(KnownNetworks)]);
            }
        }
    }
}

internal static class ReverseProxySetup
{
    public static IServiceCollection AddPulseWatchReverseProxy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ReverseProxyOptions>()
            .Bind(configuration.GetSection(ReverseProxyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ReverseProxyOptions>>((options, proxy) =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                // Exactly one proxy hop. Only the networks configured here are trusted; the default
                // (loopback) is removed so nothing is trusted by accident.
                options.ForwardLimit = 1;
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                foreach (var network in proxy.Value.KnownNetworks)
                {
                    options.KnownIPNetworks.Add(IPNetwork.Parse(network));
                }
            });

        return services;
    }

    /// <summary>Applies forwarded headers when a proxy network is configured. Must run first in the pipeline.</summary>
    public static IApplicationBuilder UsePulseWatchReverseProxy(this WebApplication app)
    {
        var proxy = app.Services.GetRequiredService<IOptions<ReverseProxyOptions>>().Value;
        return proxy.KnownNetworks.Count > 0 ? app.UseForwardedHeaders() : app;
    }
}

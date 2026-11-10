using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PulseWatch.Infrastructure.Telemetry;

public static class TelemetrySetup
{
    public const string OtlpEndpointKey = "Telemetry:OtlpEndpoint";

    /// <summary>
    /// Registers the PulseWatch meter. When <c>Telemetry:OtlpEndpoint</c> is set (for example
    /// <c>http://localhost:4317</c> for an OpenTelemetry Collector), metrics and traces are also exported over
    /// OTLP; otherwise nothing leaves the process.
    /// </summary>
    public static IServiceCollection AddPulseWatchTelemetry(
        this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddMetrics();
        services.AddSingleton<PulseWatchMetrics>();

        var endpoint = configuration[OtlpEndpointKey];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return services;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var otlp))
        {
            throw new InvalidOperationException($"{OtlpEndpointKey} must be an absolute URL, e.g. http://localhost:4317.");
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithMetrics(metrics => metrics
                .AddMeter(PulseWatchMetrics.MeterName)
                .AddOtlpExporter(o => o.Endpoint = otlp))
            .WithTracing(tracing => tracing
                .AddSource(PulseWatchMetrics.ActivitySourceName)
                .AddOtlpExporter(o => o.Endpoint = otlp));
        return services;
    }
}

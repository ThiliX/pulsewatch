using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace PulseWatch.Api.RateLimiting;

internal static class RateLimitPolicies
{
    /// <summary>Register and login, per client IP.</summary>
    public const string Auth = "auth";

    /// <summary>Monitor create/edit/delete/pause/resume, per user.</summary>
    public const string Writes = "writes";

    /// <summary>Manual check requests, per user (each monitor also has its own 60 s cooldown).</summary>
    public const string CheckNow = "check-now";
}

/// <summary>Section "RateLimiting". Permits per one-minute fixed window.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 10_000)]
    public int AuthPermitsPerMinute { get; set; } = 10;

    [Range(1, 10_000)]
    public int WritePermitsPerMinute { get; set; } = 30;

    [Range(1, 10_000)]
    public int CheckNowPermitsPerMinute { get; set; } = 10;
}

internal static class RateLimitSetup
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddPulseWatchRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await Results.Problem(
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Too many requests. Try again later.")
                    .ExecuteAsync(context.HttpContext)
                    .ConfigureAwait(false);
            };

            options.AddPolicy(RateLimitPolicies.Auth, context =>
                FixedWindow(ClientIp(context), Limits(context).AuthPermitsPerMinute));
            options.AddPolicy(RateLimitPolicies.Writes, context =>
                FixedWindow(UserOrIp(context), Limits(context).WritePermitsPerMinute));
            options.AddPolicy(RateLimitPolicies.CheckNow, context =>
                FixedWindow(UserOrIp(context), Limits(context).CheckNowPermitsPerMinute));
        });

        return services;
    }

    private static RateLimitingOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    // Behind a reverse proxy this is the real client address once ReverseProxy:KnownNetworks is set.
    private static string ClientIp(HttpContext context) =>
        "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    private static string UserOrIp(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId ? "user:" + userId : ClientIp(context);

    private static RateLimitPartition<string> FixedWindow(string key, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = Window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
}

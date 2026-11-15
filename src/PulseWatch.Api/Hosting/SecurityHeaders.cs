namespace PulseWatch.Api.Hosting;

/// <summary>
/// Security headers on every response (API, health and the web app). The web app is a Vite build with no inline
/// scripts or styles, so the CSP allows only same-origin resources. HSTS is added separately outside Development.
/// </summary>
internal static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; "
        + "connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    public const string PermissionsPolicy =
        "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";

    public static IApplicationBuilder UsePulseWatchSecurityHeaders(this IApplicationBuilder app, IHostEnvironment environment)
    {
        var development = environment.IsDevelopment();

        return app.Use((context, next) =>
        {
            // OnStarting, so the headers survive the exception handler clearing the response.
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = PermissionsPolicy;
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";

                // The development-only API reference page (Scalar) needs its own scripts and styles.
                if (!(development && IsApiReference(context.Request.Path)))
                {
                    headers.ContentSecurityPolicy = ContentSecurityPolicy;
                }

                return Task.CompletedTask;
            });

            return next(context);
        });
    }

    private static bool IsApiReference(PathString path) =>
        path.StartsWithSegments("/scalar", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase);
}

using Microsoft.AspNetCore.StaticFiles;

namespace PulseWatch.Api.Hosting;

/// <summary>
/// Serves the built web app (web/dist, copied into wwwroot by the production Dockerfile) from the same origin
/// as the API, so the auth cookie stays first-party and no CORS is needed. In development wwwroot is empty and
/// Vite serves the app instead.
/// </summary>
internal static class SpaHosting
{
    private static readonly StaticFileOptions FileOptions = new()
    {
        ContentTypeProvider = new FileExtensionContentTypeProvider(),
        OnPrepareResponse = context =>
        {
            // Vite puts content-hashed files under /assets/, so they never change. Everything else
            // (index.html above all) must be revalidated so a new release is picked up straight away.
            context.Context.Response.Headers.CacheControl =
                context.Context.Request.Path.StartsWithSegments("/assets", StringComparison.Ordinal)
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";
        },
    };

    public static IApplicationBuilder UsePulseWatchStaticFiles(this IApplicationBuilder app) =>
        app.UseStaticFiles(FileOptions);

    /// <summary>Call after mapping every API endpoint.</summary>
    public static void MapPulseWatchSpaFallback(this IEndpointRouteBuilder app)
    {
        // Unknown API and health URLs stay a 404 Problem Details response, never the app's HTML.
        app.MapFallback("/api/{**path}", NotFound);
        app.MapFallback("/health/{**path}", NotFound);

        // Any other path without a file extension is a client-side route: answer with index.html.
        app.MapFallbackToFile("{**path:nonfile}", "index.html", FileOptions);
    }

    private static IResult NotFound() => Results.Problem(statusCode: StatusCodes.Status404NotFound);
}

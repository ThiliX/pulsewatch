using Microsoft.OpenApi;
using PulseWatch.Api.Auth;

namespace PulseWatch.Api.OpenApi;

internal static class OpenApiSetup
{
    public static IServiceCollection AddPulseWatchOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "PulseWatch API";
            document.Info.Version = "v1";
            document.Info.Description =
                "Sign in with POST /api/v1/auth/login (sets an HttpOnly cookie). " +
                $"Every POST/PATCH/DELETE also needs the {AuthSetup.XsrfHeaderName} header: call " +
                "GET /api/v1/auth/antiforgery and send the returned token in that header. " +
                "Get a new token after signing in or out, because tokens are tied to the signed-in user.";

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["cookieAuth"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = AuthSetup.AuthCookieName,
                Description = "Session cookie set by POST /api/v1/auth/login.",
            };
            document.Components.SecuritySchemes["xsrfHeader"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = AuthSetup.XsrfHeaderName,
                Description = "Anti-forgery token, required on state-changing requests.",
            };
            return Task.CompletedTask;
        }));
}

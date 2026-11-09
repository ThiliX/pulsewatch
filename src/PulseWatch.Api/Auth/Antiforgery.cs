using Microsoft.AspNetCore.Antiforgery;

namespace PulseWatch.Api.Auth;

/// <summary>
/// CSRF protection for the cookie-authenticated SPA (double-submit token).
/// The SPA calls GET /api/v1/auth/antiforgery, reads the XSRF-TOKEN cookie and sends it back in the
/// X-XSRF-TOKEN header on every POST/PATCH/DELETE. The token is bound to the signed-in user, so a
/// fresh one is issued after sign-in and sign-out.
/// </summary>
internal static class Antiforgery
{
    /// <summary>Sets the XSRF-TOKEN cookie and returns the request token.</summary>
    public static string IssueToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        var requestToken = tokens.RequestToken!;
        context.Response.Cookies.Append(AuthSetup.XsrfCookieName, requestToken, new CookieOptions
        {
            HttpOnly = false, // the SPA must read it
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = "/",
            IsEssential = true,
        });
        return requestToken;
    }
}

/// <summary>Rejects state-changing requests that lack a valid anti-forgery token.</summary>
internal sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            return await next(context).ConfigureAwait(false);
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing or invalid anti-forgery token.",
                detail: $"Call GET /api/v1/auth/antiforgery and send the {AuthSetup.XsrfCookieName} cookie value in the {AuthSetup.XsrfHeaderName} header.");
        }

        return await next(context).ConfigureAwait(false);
    }
}

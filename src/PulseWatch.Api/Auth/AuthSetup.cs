using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using PulseWatch.Infrastructure.Identity;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Api.Auth;

internal static class AuthSetup
{
    public const string AuthCookieName = "pw.auth";
    public const string AntiforgeryCookieName = "pw.af";

    /// <summary>Header the SPA must send on every state-changing request.</summary>
    public const string XsrfHeaderName = "X-XSRF-TOKEN";

    /// <summary>JS-readable cookie holding the request token the SPA copies into <see cref="XsrfHeaderName"/>.</summary>
    public const string XsrfCookieName = "XSRF-TOKEN";

    public static IServiceCollection AddPulseWatchAuth(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<RegistrationOptions>().Bind(configuration.GetSection(RegistrationOptions.SectionName));

        // Cookies are Secure everywhere except Development, where the API runs on plain http://localhost.
        var securePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

        services
            .AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Length over composition rules (NIST SP 800-63B).
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 4;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<PulseWatchDbContext>()
            .AddSignInManager();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = AuthCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = securePolicy;
            options.ExpireTimeSpan = TimeSpan.FromDays(7);
            options.SlidingExpiration = true;

            // This is an API: answer 401/403 instead of redirecting to a login page.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddAntiforgery(options =>
        {
            options.HeaderName = XsrfHeaderName;
            options.Cookie.Name = AntiforgeryCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = securePolicy;
        });

        services.AddAuthorization();
        return services;
    }
}

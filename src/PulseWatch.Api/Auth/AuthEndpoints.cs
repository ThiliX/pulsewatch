using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using PulseWatch.Api.RateLimiting;
using PulseWatch.Infrastructure.Identity;

namespace PulseWatch.Api.Auth;

public sealed record RegisterRequest(string? Email, string? Password);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record MeResponse(string Id, string Email);

public sealed record AntiforgeryTokenResponse(string Token, string HeaderName);

internal static class AuthEndpoints
{
    private const int EmailMaxLength = 256;
    private const int PasswordMaxLength = 256;

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("Auth")
            .AddEndpointFilter<AntiforgeryFilter>();

        // The token is also returned in the body for API tools (e.g. the Scalar docs page). That is safe:
        // there is no CORS policy, so other sites can't read this response.
        group.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
                Results.Ok(new AntiforgeryTokenResponse(Antiforgery.IssueToken(context, antiforgery), AuthSetup.XsrfHeaderName)))
            .WithSummary("Issue an anti-forgery token. Send it in the X-XSRF-TOKEN header on POST/PATCH/DELETE. Call again after signing in or out.")
            .Produces<AntiforgeryTokenResponse>();

        group.MapPost("/register", RegisterAsync)
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithSummary("Create an account and sign in.")
            .Produces<MeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPost("/login", LoginAsync)
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithSummary("Sign in with email and password. Sets the HttpOnly auth cookie.")
            .Produces<MeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .WithSummary("Sign out.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", MeAsync)
            .RequireAuthorization()
            .WithSummary("The signed-in user.")
            .Produces<MeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        HttpContext context,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        IAntiforgery antiforgery,
        TimeProvider time)
    {
        var errors = ValidateCredentials(request.Email, request.Password);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var email = request.Email!.Trim();
        var user = new ApplicationUser { UserName = email, Email = email, CreatedAtUtc = time.GetUtcNow() };
        var result = await users.CreateAsync(user, request.Password!).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Results.ValidationProblem(MapIdentityErrors(result));
        }

        await signIn.SignInAsync(user, isPersistent: true).ConfigureAwait(false);
        await RefreshUserAndTokenAsync(context, signIn, antiforgery, user).ConfigureAwait(false);
        return Results.Created("/api/v1/auth/me", new MeResponse(user.Id, user.Email!));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        IAntiforgery antiforgery)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password)
            || request.Email.Length > EmailMaxLength || request.Password.Length > PasswordMaxLength)
        {
            return InvalidCredentials();
        }

        var user = await users.FindByEmailAsync(request.Email.Trim()).ConfigureAwait(false);
        if (user is null)
        {
            // Spend the same time as a real password check, so response timing doesn't reveal which emails exist.
            users.PasswordHasher.HashPassword(new ApplicationUser(), request.Password);
            return InvalidCredentials();
        }

        var result = await signIn.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: true)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            // Same answer for wrong password and locked account: don't reveal lockout state.
            return InvalidCredentials();
        }

        await RefreshUserAndTokenAsync(context, signIn, antiforgery, user).ConfigureAwait(false);
        return Results.Ok(new MeResponse(user.Id, user.Email!));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context, SignInManager<ApplicationUser> signIn, IAntiforgery antiforgery)
    {
        await signIn.SignOutAsync().ConfigureAwait(false);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        Antiforgery.IssueToken(context, antiforgery);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
    {
        var user = await users.GetUserAsync(principal).ConfigureAwait(false);
        return user is null
            ? Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Not signed in.")
            : Results.Ok(new MeResponse(user.Id, user.Email!));
    }

    /// <summary>The anti-forgery token is tied to the user, so issue a new one for the newly signed-in user.</summary>
    private static async Task RefreshUserAndTokenAsync(
        HttpContext context, SignInManager<ApplicationUser> signIn, IAntiforgery antiforgery, ApplicationUser user)
    {
        context.User = await signIn.CreateUserPrincipalAsync(user).ConfigureAwait(false);
        Antiforgery.IssueToken(context, antiforgery);
    }

    private static IResult InvalidCredentials() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.");

    private static Dictionary<string, string[]> ValidateCredentials(string? email, string? password)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            errors["email"] = ["Email is required."];
        }
        else if (trimmed.Length > EmailMaxLength || !new EmailAddressAttribute().IsValid(trimmed)
            || trimmed.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            errors["email"] = ["Enter a valid email address."];
        }

        if (string.IsNullOrEmpty(password))
        {
            errors["password"] = ["Password is required."];
        }
        else if (password.Length > PasswordMaxLength)
        {
            errors["password"] = [$"Password must be at most {PasswordMaxLength} characters."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> MapIdentityErrors(IdentityResult result)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var error in result.Errors)
        {
            var (field, message) = error.Code switch
            {
                // Known limitation: without email confirmation, registration reveals that an email is taken.
                // The auth rate limit makes bulk checking slow.
                "DuplicateUserName" or "DuplicateEmail" =>
                    ("email", "This email can't be used to register. If you already have an account, sign in instead."),
                _ when error.Code.StartsWith("Password", StringComparison.Ordinal) => ("password", error.Description),
                _ => ("email", error.Description),
            };
            if (!errors.TryGetValue(field, out var list))
            {
                errors[field] = list = [];
            }

            if (!list.Contains(message))
            {
                list.Add(message);
            }
        }

        return errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal);
    }
}

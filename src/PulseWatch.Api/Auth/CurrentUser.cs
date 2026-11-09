using System.Security.Claims;

namespace PulseWatch.Api.Auth;

internal static class CurrentUser
{
    /// <summary>The signed-in user's id. Only call on endpoints that require authorization.</summary>
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The request has no authenticated user id.");
}

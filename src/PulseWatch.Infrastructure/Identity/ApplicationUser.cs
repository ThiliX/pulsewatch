using Microsoft.AspNetCore.Identity;

namespace PulseWatch.Infrastructure.Identity;

/// <summary>A PulseWatch account. Credentials and password hashing are handled by ASP.NET Core Identity.</summary>
public sealed class ApplicationUser : IdentityUser
{
    public DateTimeOffset CreatedAtUtc { get; set; }
}

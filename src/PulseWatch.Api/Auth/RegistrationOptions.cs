namespace PulseWatch.Api.Auth;

/// <summary>
/// Section "Registration". A self-hosted PulseWatch should not let strangers sign up: every account can make
/// the server send requests to URLs of its choosing. Deployment docs: register your own account, then set
/// <see cref="Enabled"/> to false.
/// </summary>
public sealed class RegistrationOptions
{
    public const string SectionName = "Registration";

    /// <summary>When false, POST /api/v1/auth/register answers 403. Existing accounts can still sign in.</summary>
    public bool Enabled { get; set; } = true;
}

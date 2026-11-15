using System.ComponentModel.DataAnnotations;
using PulseWatch.Domain.Common;

namespace PulseWatch.Application.Options;

public enum EmailProvider
{
    /// <summary>Writes emails to the log instead of sending them. The development default.</summary>
    Logging = 0,

    /// <summary>Sends through an SMTP server configured in the "Email" section.</summary>
    Smtp = 1,
}

/// <summary>Outbox delivery settings (section "Notifications"). Validated at worker startup.</summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public EmailProvider Provider { get; set; } = EmailProvider.Logging;

    /// <summary>Delivery attempts per message before it is marked Exhausted.</summary>
    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 6;

    /// <summary>How often the dispatcher looks for messages to send.</summary>
    [Range(1, 300)]
    public int PollIntervalSeconds { get; set; } = 10;

    /// <summary>Maximum messages claimed per poll.</summary>
    [Range(1, 200)]
    public int BatchSize { get; set; } = 20;

    /// <summary>Messages sent at the same time by one worker.</summary>
    [Range(1, 20)]
    public int MaxConcurrentSends { get; set; } = 5;

    /// <summary>Hard limit for one send, including connecting and authenticating.</summary>
    [Range(5, 120)]
    public int SendTimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// SMTP settings (section "Email"), used when <see cref="NotificationOptions.Provider"/> is Smtp.
/// Keep <see cref="Password"/> in user-secrets or an environment variable, never in appsettings.
/// Nothing in PulseWatch logs these values.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    [StringLength(253)]
    public string? Host { get; set => field = Blank(value); }

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    [StringLength(256)]
    public string? Username { get; set => field = Blank(value); }

    [StringLength(512)]
    public string? Password { get; set => field = Blank(value); }

    [EmailAddress]
    [StringLength(256)]
    public string? FromAddress { get; set => field = Blank(value); }

    [StringLength(100)]
    public string FromName { get; set; } = "PulseWatch";

    // An empty environment variable (e.g. SMTP_USERNAME= in .env) means "not set", not "an empty value".
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// true (default): require STARTTLS. false: plain connection, for a local test server such as Mailpit.
    /// Port 465 always uses TLS from the first byte.
    /// </summary>
    public bool UseStartTls { get; set; } = true;
}

/// <summary>General app settings (section "App").</summary>
public sealed class AppOptions : IValidatableObject
{
    public const string SectionName = "App";

    /// <summary>Where users open the dashboard. Used for links in emails.</summary>
    [Required]
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";

    public Uri GetPublicBaseUri() => new(PublicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!AbsoluteUrl.TryParse(PublicBaseUrl, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            yield return new ValidationResult(
                "PublicBaseUrl must be an absolute http(s) URL without a query string or fragment.",
                [nameof(PublicBaseUrl)]);
        }
    }
}

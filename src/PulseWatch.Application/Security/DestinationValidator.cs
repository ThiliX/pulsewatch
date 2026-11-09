using System.Net;
using System.Text.RegularExpressions;
using PulseWatch.Domain.Monitoring;

namespace PulseWatch.Application.Security;

/// <summary>Outcome of checking whether PulseWatch may send requests to a URL.</summary>
public sealed record DestinationCheck(bool IsAllowed, string? ErrorCode, string? Message, IReadOnlyList<IPAddress> Addresses)
{
    public static DestinationCheck Allow(IReadOnlyList<IPAddress> addresses) => new(true, null, null, addresses);

    public static DestinationCheck Reject(string code, string message) => new(false, code, message, []);
}

public static class DestinationErrorCodes
{
    public const string InvalidUrl = "invalid_url";
    public const string UnsupportedScheme = "unsupported_scheme";
    public const string CredentialsInUrl = "credentials_in_url";
    public const string PortNotAllowed = "port_not_allowed";
    public const string BlockedHost = "blocked_host";
    public const string AmbiguousAddress = "ambiguous_address";
    public const string BlockedAddress = "blocked_address";
    public const string DnsFailure = "dns_failure";
}

public interface IDestinationValidator
{
    Task<DestinationCheck> ValidateAsync(string url, CancellationToken cancellationToken);
}

/// <summary>
/// SSRF layers 1 and 2: URL rules, host-name rules, and "every resolved address must be public".
/// Used when a monitor is created or edited and (from Milestone 3) for every redirect.
/// This alone does not stop DNS rebinding: the probe must also check the address it actually
/// connects to (layer 3), and deployments should restrict outbound traffic (layer 4).
/// </summary>
public sealed partial class DestinationValidator(
    IDnsResolver dnsResolver, IReadOnlyCollection<int> allowedPorts, IAddressPolicy? addressPolicy = null)
    : IDestinationValidator
{
    private readonly IAddressPolicy _policy = addressPolicy ?? PublicOnlyAddressPolicy.Instance;

    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(3);

    private static readonly string[] BlockedHostSuffixes =
        [".localhost", ".local", ".internal", ".home.arpa", ".intranet", ".lan", ".corp"];

    private static readonly string[] BlockedHostNames =
        ["localhost", "metadata.google.internal", "metadata", "instance-data"];

    public async Task<DestinationCheck> ValidateAsync(string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > MonitorLimits.UrlMaxLength
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return DestinationCheck.Reject(DestinationErrorCodes.InvalidUrl, "Enter a valid absolute URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return DestinationCheck.Reject(DestinationErrorCodes.UnsupportedScheme, "Only http and https URLs can be monitored.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return DestinationCheck.Reject(DestinationErrorCodes.CredentialsInUrl, "URLs must not contain a username or password.");
        }

        if (!allowedPorts.Contains(uri.Port))
        {
            return DestinationCheck.Reject(
                DestinationErrorCodes.PortNotAllowed,
                $"Port {uri.Port} is not allowed. Allowed ports: {string.Join(", ", allowedPorts.Order())}.");
        }

        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            return CheckIpLiteral(uri, url);
        }

        if (uri.HostNameType != UriHostNameType.Dns)
        {
            return DestinationCheck.Reject(DestinationErrorCodes.InvalidUrl, "The URL's host name is not valid.");
        }

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (IsBlockedHostName(host))
        {
            return DestinationCheck.Reject(
                DestinationErrorCodes.BlockedHost, "Local and internal host names cannot be monitored. Use a public host name.");
        }

        return await ResolveAndCheckAsync(host, cancellationToken).ConfigureAwait(false);
    }

    private DestinationCheck CheckIpLiteral(Uri uri, string originalUrl)
    {
        // Uri turns odd spellings like "2130706433", "0x7f.1" or "127.1" into 127.0.0.1. Those forms
        // are a classic way to sneak past filters, so only plain dotted-decimal IPv4 is accepted.
        if (uri.HostNameType == UriHostNameType.IPv4 && !StrictIPv4().IsMatch(RawHost(originalUrl.Trim()) ?? string.Empty))
        {
            return DestinationCheck.Reject(
                DestinationErrorCodes.AmbiguousAddress, "Write IPv4 addresses in the usual dotted form, for example 203.0.113.10.");
        }

        if (!IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out var address))
        {
            return DestinationCheck.Reject(DestinationErrorCodes.InvalidUrl, "The URL's IP address is not valid.");
        }

        return _policy.IsAllowed(address)
            ? DestinationCheck.Allow([address])
            : DestinationCheck.Reject(
                DestinationErrorCodes.BlockedAddress, "Private, local and reserved IP addresses cannot be monitored.");
    }

    private async Task<DestinationCheck> ResolveAndCheckAsync(string host, CancellationToken cancellationToken)
    {
        IReadOnlyList<IPAddress> addresses;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DnsTimeout);
        try
        {
            addresses = await dnsResolver.ResolveAsync(host, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return DestinationCheck.Reject(DestinationErrorCodes.DnsFailure, "The host name could not be resolved.");
        }

        if (addresses.Count == 0)
        {
            return DestinationCheck.Reject(DestinationErrorCodes.DnsFailure, "The host name could not be resolved.");
        }

        // Reject if ANY answer is non-public: an attacker controlling DNS could otherwise mix in an internal address.
        return addresses.All(_policy.IsAllowed)
            ? DestinationCheck.Allow(addresses)
            : DestinationCheck.Reject(
                DestinationErrorCodes.BlockedAddress, "This host name points to a private, local or reserved address.");
    }

    private static bool IsBlockedHostName(string host) =>
        !host.Contains('.', StringComparison.Ordinal)
        || BlockedHostNames.Contains(host, StringComparer.Ordinal)
        || BlockedHostSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.Ordinal));

    /// <summary>The host exactly as typed (before Uri normalisation), without user-info or port.</summary>
    private static string? RawHost(string url)
    {
        var start = url.IndexOf("://", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += 3;
        var end = url.IndexOfAny(['/', '?', '#', '\\'], start);
        var authority = end < 0 ? url[start..] : url[start..end];
        var at = authority.LastIndexOf('@');
        if (at >= 0)
        {
            authority = authority[(at + 1)..];
        }

        var colon = authority.LastIndexOf(':');
        return colon >= 0 ? authority[..colon] : authority;
    }

    [GeneratedRegex(@"^(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}$")]
    private static partial Regex StrictIPv4();
}

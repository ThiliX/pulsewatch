using PulseWatch.Domain.Common;

namespace PulseWatch.Domain.Monitoring;

/// <summary>
/// Syntax rules for a monitored URL. This is NOT the SSRF check: destination safety
/// (private IPs, DNS answers, redirects) is enforced separately before any request is made.
/// </summary>
public static class MonitorUrl
{
    public static string Normalize(string? value)
    {
        var text = Guard.Text(value, MonitorLimits.UrlMaxLength, "Url");

        if (!AbsoluteUrl.TryParse(text, out var uri))
        {
            throw new DomainException("Url must be an absolute URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new DomainException("Url must use http or https.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new DomainException("Url must not contain a username or password.");
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            throw new DomainException("Url must not contain a fragment (#...).");
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            throw new DomainException("Url must include a host name.");
        }

        return uri.AbsoluteUri;
    }
}

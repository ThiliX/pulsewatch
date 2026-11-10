using System.Globalization;
using System.Net;
using System.Text;
using PulseWatch.Domain.Common;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Notifications;

namespace PulseWatch.Application.Notifications;

/// <summary>Stored data needed to write one incident email. Loaded by the outbox store.</summary>
public sealed record NotificationEmailData(
    NotificationEventType EventType,
    string Recipient,
    Guid IncidentId,
    string MonitorName,
    string MonitorUrl,
    CheckOutcome OpeningOutcome,
    int? OpeningStatusCode,
    DateTimeOffset FirstFailureAtUtc,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    int FailureCount);

/// <summary>
/// Builds the plain-text and HTML email for an incident event. Safety rules:
/// <list type="bullet">
/// <item>The monitored URL is shown as scheme, host, port and path only. The query string is dropped
/// because it often carries API keys.</item>
/// <item>Everything placed in HTML is HTML-encoded.</item>
/// <item>The subject has control characters (including CR/LF) removed and is length-limited, so a
/// monitor name can't inject mail headers.</item>
/// </list>
/// </summary>
public static class NotificationEmailBuilder
{
    public const int SubjectNameMaxLength = 80;

    public static EmailMessage Build(NotificationEmailData data, Uri publicBaseUri)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(publicBaseUri);

        var opened = data.EventType == NotificationEventType.IncidentOpened;
        var name = SingleLine(data.MonitorName, SubjectNameMaxLength);
        var target = DisplayUrl(data.MonitorUrl);
        var link = new Uri(publicBaseUri, $"incidents/{data.IncidentId:D}").AbsoluteUri;
        var evidence = Evidence(data.OpeningOutcome, data.OpeningStatusCode);

        var subject = opened ? $"[PulseWatch] DOWN: {name}" : $"[PulseWatch] RECOVERED: {name}";
        var headline = opened ? $"{name} is down" : $"{name} has recovered";

        var facts = new List<(string Label, string Value)>
        {
            ("Monitor", name),
            ("Endpoint", target),
            (opened ? "Failure" : "Original failure", evidence),
            ("First failed check", Time(data.FirstFailureAtUtc)),
            ("Incident opened", Time(data.OpenedAtUtc)),
        };
        if (!opened && data.ResolvedAtUtc is { } resolved)
        {
            facts.Add(("Recovered", Time(resolved)));
            facts.Add(("Outage length", Duration(resolved - data.FirstFailureAtUtc)));
            facts.Add(("Failed checks", data.FailureCount.ToString(CultureInfo.InvariantCulture)));
        }

        const string Note = "Checked from a single location. Times are UTC.";

        var text = new StringBuilder()
            .AppendLine(headline)
            .AppendLine();
        foreach (var (label, value) in facts)
        {
            text.Append(label).Append(": ").AppendLine(value);
        }

        text.AppendLine()
            .Append("Details: ").AppendLine(link)
            .AppendLine()
            .AppendLine(Note);

        var html = new StringBuilder()
            .Append("<!doctype html><html><body style=\"font-family:sans-serif;color:#1f2328\">")
            .Append("<h2 style=\"margin:0 0 12px\">").Append(Html(headline)).Append("</h2>")
            .Append("<table cellpadding=\"4\" style=\"border-collapse:collapse\">");
        foreach (var (label, value) in facts)
        {
            html.Append("<tr><th align=\"left\" style=\"padding-right:16px\">").Append(Html(label))
                .Append("</th><td>").Append(Html(value)).Append("</td></tr>");
        }

        html.Append("</table>")
            .Append("<p><a href=\"").Append(Html(link)).Append("\">View the incident</a></p>")
            .Append("<p style=\"color:#656d76;font-size:12px\">").Append(Html(Note)).Append("</p>")
            .Append("</body></html>");

        return new EmailMessage(SingleLine(data.Recipient, 256), subject, text.ToString(), html.ToString());
    }

    /// <summary>scheme://host[:port]/path, without query string, fragment or user info.</summary>
    public static string DisplayUrl(string url)
    {
        if (!AbsoluteUrl.TryParse(url, out var uri))
        {
            return "(invalid URL)";
        }

        return SingleLine(uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped), 500);
    }

    /// <summary>Removes control characters (CR, LF, tabs, …) and trims to <paramref name="maxLength"/>.</summary>
    public static string SingleLine(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        var clean = builder.ToString().Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength].TrimEnd() + "…";
    }

    private static string Evidence(CheckOutcome outcome, int? statusCode) => outcome switch
    {
        CheckOutcome.HttpStatusFailure when statusCode is { } code =>
            string.Create(CultureInfo.InvariantCulture, $"Unexpected HTTP status {code}"),
        CheckOutcome.HttpStatusFailure => "Unexpected HTTP status",
        CheckOutcome.Timeout => "Timed out",
        CheckOutcome.DnsFailure => "DNS lookup failed",
        CheckOutcome.TlsFailure => "TLS/certificate error",
        CheckOutcome.ConnectionFailure => "Connection failed",
        CheckOutcome.RedirectRejected => "Redirect rejected",
        CheckOutcome.DestinationBlocked => "Destination blocked by PulseWatch's network rules",
        CheckOutcome.ResponseTooLarge => "Response too large",
        CheckOutcome.Success => "Success",
        _ => "Unexpected error",
    };

    private static string Time(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours} h {span.Minutes} min")
            : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes} min {span.Seconds} s");
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}

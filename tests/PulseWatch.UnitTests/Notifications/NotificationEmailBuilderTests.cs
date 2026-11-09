using PulseWatch.Application.Notifications;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Notifications;

namespace PulseWatch.UnitTests.Notifications;

public class NotificationEmailBuilderTests
{
    private static readonly Uri BaseUri = new("https://pulse.example.com/");
    private static readonly Guid IncidentId = Guid.Parse("0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");
    private static readonly DateTimeOffset FirstFailure = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static NotificationEmailData Data(
        NotificationEventType eventType = NotificationEventType.IncidentOpened,
        string name = "Orders API",
        string url = "https://api.example.com:8443/health?api_key=SECRET123&x=1") =>
        new(
            eventType,
            "owner@example.com",
            IncidentId,
            name,
            url,
            CheckOutcome.HttpStatusFailure,
            503,
            FirstFailure,
            FirstFailure.AddMinutes(2),
            eventType == NotificationEventType.IncidentResolved ? FirstFailure.AddMinutes(75).AddSeconds(5) : null,
            12);

    [Fact]
    public void Opened_email_has_a_down_subject_evidence_times_and_a_link()
    {
        var email = NotificationEmailBuilder.Build(Data(), BaseUri);

        Assert.Equal("owner@example.com", email.To);
        Assert.Equal("[PulseWatch] DOWN: Orders API", email.Subject);
        Assert.Contains("Unexpected HTTP status 503", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("2026-11-01 12:00:00 UTC", email.TextBody, StringComparison.Ordinal);
        Assert.Contains($"https://pulse.example.com/incidents/{IncidentId:D}", email.TextBody, StringComparison.Ordinal);
        Assert.Contains($"href=\"https://pulse.example.com/incidents/{IncidentId:D}\"", email.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Recovered", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolved_email_shows_recovery_time_outage_length_and_failure_count()
    {
        var email = NotificationEmailBuilder.Build(Data(NotificationEventType.IncidentResolved), BaseUri);

        Assert.Equal("[PulseWatch] RECOVERED: Orders API", email.Subject);
        Assert.Contains("Recovered: 2026-11-01 13:15:05 UTC", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Outage length: 1 h 15 min", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Failed checks: 12", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void The_query_string_never_appears_in_the_email()
    {
        var email = NotificationEmailBuilder.Build(Data(), BaseUri);

        Assert.Contains("https://api.example.com:8443/health", email.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET123", email.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET123", email.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Monitor_names_are_html_encoded()
    {
        var email = NotificationEmailBuilder.Build(Data(name: "<script>alert(1)</script> & co"), BaseUri);

        Assert.DoesNotContain("<script>", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; co", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_breaks_in_the_name_cannot_inject_headers()
    {
        var email = NotificationEmailBuilder.Build(Data(name: "API\r\nBcc: victim@example.com"), BaseUri);

        Assert.DoesNotContain('\r', email.Subject);
        Assert.DoesNotContain('\n', email.Subject);
        Assert.Equal("[PulseWatch] DOWN: API  Bcc: victim@example.com", email.Subject);
    }

    [Fact]
    public void Long_names_are_shortened_in_the_subject()
    {
        var email = NotificationEmailBuilder.Build(Data(name: new string('a', 200)), BaseUri);

        Assert.True(email.Subject.Length <= "[PulseWatch] DOWN: ".Length + NotificationEmailBuilder.SubjectNameMaxLength + 1);
        Assert.EndsWith("…", email.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void A_base_url_with_a_path_keeps_the_path_in_links()
    {
        var email = NotificationEmailBuilder.Build(Data(), new Uri("https://example.com/pulse/"));

        Assert.Contains($"https://example.com/pulse/incidents/{IncidentId:D}", email.TextBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://h.example/p?q=1#f", "https://h.example/p")]
    [InlineData("http://h.example", "http://h.example/")]
    [InlineData("not a url", "(invalid URL)")]
    public void Display_url_drops_query_and_fragment(string url, string expected) =>
        Assert.Equal(expected, NotificationEmailBuilder.DisplayUrl(url));
}

using PulseWatch.Domain.Common;
using PulseWatch.Domain.Monitoring;

namespace PulseWatch.UnitTests.Domain;

public class EndpointMonitorCreationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static EndpointMonitor Create(
        string name = "Orders API",
        string url = "https://api.example.com/health",
        MonitorSettings? settings = null,
        DateTimeOffset? now = null) =>
        EndpointMonitor.Create("user-1", name, url, settings ?? MonitorSettings.Default, now ?? Now);

    [Fact]
    public void Creates_an_enabled_monitor_due_immediately_with_defaults()
    {
        var monitor = Create(name: "  Orders API  ");

        Assert.Equal("Orders API", monitor.Name);
        Assert.True(monitor.IsEnabled);
        Assert.Equal(MonitorState.Unknown, monitor.State);
        Assert.Equal(Now, monitor.NextCheckAtUtc);
        Assert.Equal(Now, monitor.CreatedAtUtc);
        Assert.Equal(MonitorSettings.Default, monitor.Settings);
        Assert.NotEqual(Guid.Empty, monitor.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_an_empty_name(string name) =>
        Assert.Throws<DomainException>(() => Create(name: name));

    [Fact]
    public void Rejects_a_name_over_100_characters() =>
        Assert.Throws<DomainException>(() => Create(name: new string('a', 101)));

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    [InlineData("not a url")]
    [InlineData("https://user:secret@example.com/")]
    [InlineData("https://user@example.com/")]
    [InlineData("https://example.com/page#section")]
    public void Rejects_invalid_or_unsafe_url_syntax(string url) =>
        Assert.Throws<DomainException>(() => Create(url: url));

    [Fact]
    public void Rejects_a_url_over_2048_characters() =>
        Assert.Throws<DomainException>(() => Create(url: "https://example.com/" + new string('a', 2048)));

    [Theory]
    [InlineData("https://API.Example.com/health?x=1", "https://api.example.com/health?x=1")]
    [InlineData("http://example.com", "http://example.com/")]
    public void Normalizes_the_url(string input, string expected) =>
        Assert.Equal(expected, Create(url: input).Url);

    [Fact]
    public void Rejects_a_non_utc_timestamp() =>
        Assert.Throws<DomainException>(() => Create(now: new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(5.5))));

    public static TheoryData<MonitorSettings> InvalidSettings => new()
    {
        MonitorSettings.Default with { IntervalSeconds = 59 },
        MonitorSettings.Default with { IntervalSeconds = 3601 },
        MonitorSettings.Default with { TimeoutMs = 999 },
        MonitorSettings.Default with { TimeoutMs = 15001 },
        MonitorSettings.Default with { IntervalSeconds = 60, TimeoutMs = 60000 },
        MonitorSettings.Default with { FailureThreshold = 0 },
        MonitorSettings.Default with { FailureThreshold = 11 },
        MonitorSettings.Default with { RecoveryThreshold = 0 },
        MonitorSettings.Default with { ExpectedStatusMin = 99 },
        MonitorSettings.Default with { ExpectedStatusMax = 600 },
        MonitorSettings.Default with { ExpectedStatusMin = 300, ExpectedStatusMax = 200 },
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void Rejects_settings_outside_the_hard_limits(MonitorSettings settings) =>
        Assert.Throws<DomainException>(() => Create(settings: settings));

    [Theory]
    [InlineData(200, true)]
    [InlineData(299, true)]
    [InlineData(199, false)]
    [InlineData(300, false)]
    [InlineData(503, false)]
    public void Expected_status_uses_an_inclusive_range(int status, bool expected) =>
        Assert.Equal(expected, Create().IsExpectedStatus(status));
}

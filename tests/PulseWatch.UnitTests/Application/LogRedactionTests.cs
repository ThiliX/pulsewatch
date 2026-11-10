using PulseWatch.Application.Telemetry;

namespace PulseWatch.UnitTests.Application;

public class LogRedactionTests
{
    [Theory]
    [InlineData("https://api.example.com/v1/accounts/42?token=secret", "https://api.example.com")]
    [InlineData("http://api.example.com:8080/health", "http://api.example.com:8080")]
    [InlineData("https://api.example.com:443/health", "https://api.example.com")]
    [InlineData("https://user:pass@api.example.com/", "https://api.example.com")]
    [InlineData("not a url", "(invalid url)")]
    [InlineData(null, "(invalid url)")]
    public void Only_scheme_host_and_non_default_port_are_kept(string? url, string expected) =>
        Assert.Equal(expected, LogRedaction.UrlOrigin(url));
}

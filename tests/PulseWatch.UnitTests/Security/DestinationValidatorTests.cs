using PulseWatch.Application.Security;
using static PulseWatch.Application.Security.DestinationErrorCodes;

namespace PulseWatch.UnitTests.Security;

public class DestinationValidatorTests
{
    private static readonly int[] AllowedPorts = [80, 443, 8080, 8443];

    private readonly FakeDnsResolver _dns = new FakeDnsResolver()
        .Answer("api.example.com", "93.184.216.34")
        .Answer("dual.example.com", "93.184.216.34", "2606:2800:220:1:248:1893:25c8:1946")
        .Answer("mixed.example.com", "93.184.216.34", "10.0.0.5")
        .Answer("rebind.example.com", "127.0.0.1")
        .Answer("metadata-alias.example.com", "169.254.169.254")
        .Answer("mapped.example.com", "::ffff:192.168.1.1")
        .Answer("empty.example.com")
        .Answer("xn--bcher-kva.example", "93.184.216.34")
        .Throw("slow.example.com", new TimeoutException());

    private Task<DestinationCheck> Validate(string url) =>
        new DestinationValidator(_dns, AllowedPorts).ValidateAsync(url, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("https://api.example.com/health")]
    [InlineData("http://api.example.com:8080/status?full=1")]
    [InlineData("https://dual.example.com/")]
    [InlineData("https://API.EXAMPLE.COM./health")] // case and trailing dot normalised
    [InlineData("https://8.8.8.8/")]
    [InlineData("https://[2606:4700:4700::1111]/")]
    [InlineData("https://bücher.example/")]          // IDN resolved via punycode
    public async Task Allows_public_destinations(string url)
    {
        var result = await Validate(url);

        Assert.True(result.IsAllowed, result.Message);
        Assert.NotEmpty(result.Addresses);
    }

    [Theory]
    [InlineData("", InvalidUrl)]
    [InlineData("not a url", InvalidUrl)]
    [InlineData("/relative", InvalidUrl)]
    [InlineData("file:///etc/passwd", UnsupportedScheme)]
    [InlineData("ftp://api.example.com/", UnsupportedScheme)]
    [InlineData("gopher://api.example.com/", UnsupportedScheme)]
    [InlineData("javascript:alert(1)", UnsupportedScheme)]
    [InlineData("https://user:pass@api.example.com/", CredentialsInUrl)]
    [InlineData("https://user@api.example.com/", CredentialsInUrl)]
    [InlineData("https://api.example.com:22/", PortNotAllowed)]
    [InlineData("http://api.example.com:6379/", PortNotAllowed)]
    public async Task Rejects_bad_urls(string url, string expectedCode)
    {
        var result = await Validate(url);

        Assert.False(result.IsAllowed);
        Assert.Equal(expectedCode, result.ErrorCode);
    }

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://LOCALHOST/")]
    [InlineData("http://localhost./")]
    [InlineData("http://app.localhost/")]
    [InlineData("http://printer.local/")]
    [InlineData("http://db.internal/")]
    [InlineData("http://metadata.google.internal/computeMetadata/v1/")]
    [InlineData("http://router.home.arpa/")]
    [InlineData("http://intranet/")]
    [InlineData("http://metadata/")]
    public async Task Rejects_local_and_internal_host_names_without_resolving_them(string url)
    {
        var result = await Validate(url);

        Assert.False(result.IsAllowed);
        Assert.Equal(BlockedHost, result.ErrorCode);
        Assert.Empty(_dns.Queries);
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.1.2.3/")]
    [InlineData("http://192.168.0.1:8080/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::ffff:10.0.0.1]/")]
    [InlineData("http://[::ffff:7f00:1]/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://[fe80::1]/")]
    public async Task Rejects_non_public_ip_literals(string url)
    {
        var result = await Validate(url);

        Assert.False(result.IsAllowed);
        Assert.Equal(BlockedAddress, result.ErrorCode);
    }

    [Theory]
    [InlineData("http://2130706433/")]   // decimal 127.0.0.1
    [InlineData("http://0x7f000001/")]   // hex
    [InlineData("http://0x7f.1/")]
    [InlineData("http://127.1/")]        // short form
    [InlineData("http://0177.0.0.1/")]   // octal
    [InlineData("http://134744072/")]    // decimal form of public 8.8.8.8 is still refused
    public async Task Rejects_unusual_ipv4_spellings(string url)
    {
        var result = await Validate(url);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.ErrorCode, new[] { AmbiguousAddress, BlockedAddress, BlockedHost });
    }

    [Theory]
    [InlineData("https://mixed.example.com/")]          // one public and one private answer
    [InlineData("https://rebind.example.com/")]         // public-looking name pointing at loopback
    [InlineData("https://metadata-alias.example.com/")] // name pointing at the metadata service
    [InlineData("https://mapped.example.com/")]         // AAAA answer wrapping a private IPv4
    public async Task Rejects_host_names_that_resolve_to_any_blocked_address(string url)
    {
        var result = await Validate(url);

        Assert.False(result.IsAllowed);
        Assert.Equal(BlockedAddress, result.ErrorCode);
        Assert.DoesNotContain("10.0.0.5", result.Message, StringComparison.Ordinal); // never echo internal IPs
    }

    [Theory]
    [InlineData("https://unknown.example.com/")]
    [InlineData("https://empty.example.com/")]
    [InlineData("https://slow.example.com/")]
    public async Task Rejects_host_names_that_cannot_be_resolved(string url)
    {
        var result = await Validate(url);

        Assert.False(result.IsAllowed);
        Assert.Equal(DnsFailure, result.ErrorCode);
    }
}

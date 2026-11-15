using System.Net;
using PulseWatch.Application.Security;

namespace PulseWatch.UnitTests.Security;

public class PublicAddressPolicyTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("100.64.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("169.254.169.254")] // cloud metadata
    [InlineData("169.254.1.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.8")]
    [InlineData("192.0.2.10")]
    [InlineData("192.88.99.1")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.7")]
    [InlineData("203.0.113.5")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.250")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::127.0.0.1")]            // deprecated IPv4-compatible
    [InlineData("::ffff:127.0.0.1")]       // IPv4-mapped loopback
    [InlineData("::ffff:10.0.0.1")]        // IPv4-mapped private
    [InlineData("::ffff:169.254.169.254")] // IPv4-mapped metadata
    [InlineData("64:ff9b::a00:1")]         // NAT64 wrapping 10.0.0.1
    [InlineData("64:ff9b:1::1")]
    [InlineData("100::1")]
    [InlineData("2001::1")]                // Teredo
    [InlineData("2001:db8::1")]
    [InlineData("2002:a00:1::1")]          // 6to4 wrapping 10.0.0.1
    [InlineData("fc00::1")]
    [InlineData("fd12:3456:789a::1")]
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:2::1")]              // benchmarking (inside 2001::/23)
    [InlineData("2001:20::1")]             // ORCHIDv2
    [InlineData("3fff::1")]                // documentation (RFC 9637)
    [InlineData("5f00::1")]                // SRv6 SIDs
    [InlineData("4000::1")]                // outside global unicast 2000::/3
    [InlineData("1::1")]                   // unassigned, outside 2000::/3
    public void Blocks_non_public_addresses(string address) =>
        Assert.False(PublicAddressPolicy.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("172.15.255.255")]  // just below 172.16/12
    [InlineData("172.32.0.1")]      // just above 172.16/12
    [InlineData("100.63.255.255")]  // just below 100.64/10
    [InlineData("::ffff:8.8.8.8")]  // IPv4-mapped public
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2a00:1450:4001:80b::200e")]
    public void Allows_public_addresses(string address) =>
        Assert.True(PublicAddressPolicy.IsPublic(IPAddress.Parse(address)));

    [Fact]
    public void Blocks_ipv6_addresses_with_a_scope_id() =>
        Assert.False(PublicAddressPolicy.IsPublic(IPAddress.Parse("2606:4700:4700::1111%3")));
}

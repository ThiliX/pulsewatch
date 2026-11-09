using System.Net;
using System.Net.Sockets;

namespace PulseWatch.Application.Security;

/// <summary>
/// SSRF layer 2: decides whether an IP address is a public internet destination.
/// Everything private, local, reserved, multicast or used for documentation is blocked,
/// including IPv6 forms that can wrap an internal IPv4 address.
/// </summary>
public static class PublicAddressPolicy
{
    private static readonly IPNetwork[] BlockedV4 = Parse(
        "0.0.0.0/8",          // "this network"
        "10.0.0.0/8",         // private
        "100.64.0.0/10",      // carrier-grade NAT
        "127.0.0.0/8",        // loopback
        "169.254.0.0/16",     // link-local, includes cloud metadata 169.254.169.254
        "172.16.0.0/12",      // private
        "192.0.0.0/24",       // IETF protocol assignments
        "192.0.2.0/24",       // documentation (TEST-NET-1)
        "192.88.99.0/24",     // 6to4 relay anycast
        "192.168.0.0/16",     // private
        "198.18.0.0/15",      // benchmarking
        "198.51.100.0/24",    // documentation (TEST-NET-2)
        "203.0.113.0/24",     // documentation (TEST-NET-3)
        "224.0.0.0/4",        // multicast
        "240.0.0.0/4");       // reserved, includes 255.255.255.255 broadcast

    private static readonly IPNetwork[] BlockedV6 = Parse(
        "::/96",              // unspecified, loopback and deprecated IPv4-compatible addresses
        "::ffff:0:0/96",      // IPv4-mapped (normally unwrapped first; blocked here as a safety net)
        "64:ff9b::/96",       // NAT64, can wrap internal IPv4
        "64:ff9b:1::/48",     // local-use NAT64
        "100::/64",           // discard-only
        "2001::/32",          // Teredo, can wrap internal IPv4
        "2001:db8::/32",      // documentation
        "2002::/16",          // 6to4, can wrap internal IPv4
        "fc00::/7",           // unique local (private)
        "fe80::/10",          // link-local
        "fec0::/10",          // deprecated site-local
        "ff00::/8");          // multicast

    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => !BlockedV4.Any(network => network.Contains(address)),
            AddressFamily.InterNetworkV6 => address.ScopeId == 0 && !BlockedV6.Any(network => network.Contains(address)),
            _ => false,
        };
    }

    private static IPNetwork[] Parse(params string[] networks) =>
        networks.Select(n => IPNetwork.Parse(n)).ToArray();
}

using System.Net;

namespace PulseWatch.Application.Security;

/// <summary>Decides which IP addresses PulseWatch may connect to.</summary>
public interface IAddressPolicy
{
    bool IsAllowed(IPAddress address);
}

/// <summary>
/// The only policy used outside tests: public internet addresses only.
/// Integration tests that need a local target swap in their own policy through DI in the test project;
/// there is deliberately no configuration switch that could relax this in production.
/// </summary>
public sealed class PublicOnlyAddressPolicy : IAddressPolicy
{
    public static PublicOnlyAddressPolicy Instance { get; } = new();

    public bool IsAllowed(IPAddress address) => PublicAddressPolicy.IsPublic(address);
}

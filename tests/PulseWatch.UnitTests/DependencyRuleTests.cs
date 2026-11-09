using System.Reflection;
using PulseWatch.Application;
using PulseWatch.Domain;

namespace PulseWatch.UnitTests;

/// <summary>
/// Enforces the dependency direction from the plan: Domain depends on nothing,
/// Application depends only on Domain. Neither may pull in EF Core, ASP.NET Core or Infrastructure.
/// </summary>
public class DependencyRuleTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "PulseWatch.Infrastructure",
        "PulseWatch.Api",
        "PulseWatch.Worker",
    ];

    [Fact]
    public void Domain_references_only_the_base_class_library()
    {
        var references = ReferencedNames(typeof(DomainAssemblyMarker).Assembly);

        Assert.All(references, name => Assert.True(
            name.StartsWith("System", StringComparison.Ordinal) || name == "netstandard",
            $"Domain must not reference '{name}'."));
    }

    [Fact]
    public void Application_does_not_reference_infrastructure_or_frameworks()
    {
        var references = ReferencedNames(typeof(ApplicationAssemblyMarker).Assembly);

        Assert.DoesNotContain(references, name =>
            ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
    }

    private static string[] ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();
}

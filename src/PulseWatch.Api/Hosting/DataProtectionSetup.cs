using Microsoft.AspNetCore.DataProtection;

namespace PulseWatch.Api.Hosting;

/// <summary>
/// Section "DataProtection". The auth and anti-forgery cookies are encrypted with ASP.NET Core Data Protection
/// keys. In a container the default key folder is thrown away on every restart, which would sign everyone out,
/// so production stores the keys in a volume (docker-compose.prod.yml mounts one at /keys).
/// </summary>
public sealed class DataProtectionSettings
{
    public const string SectionName = "DataProtection";

    /// <summary>Folder for the key ring. Empty = the ASP.NET Core default (fine for development).</summary>
    public string? KeysPath { get; set; }
}

internal static class DataProtectionSetup
{
    public static IServiceCollection AddPulseWatchDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(DataProtectionSettings.SectionName).Get<DataProtectionSettings>()
            ?? new DataProtectionSettings();

        var builder = services.AddDataProtection().SetApplicationName("PulseWatch");
        if (!string.IsNullOrWhiteSpace(settings.KeysPath))
        {
            // Keys are stored unencrypted at rest; the volume must only be readable by the API container
            // (docs/deployment.md). Anyone who can read them can forge sign-in cookies.
            builder.PersistKeysToFileSystem(new DirectoryInfo(settings.KeysPath));
        }

        return services;
    }
}

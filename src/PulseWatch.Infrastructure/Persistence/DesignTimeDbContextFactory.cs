using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PulseWatch.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef migrations add/remove/script`, which never connect to the database.
/// Apply migrations with the API's --migrate switch (Development) or a migration bundle.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PulseWatchDbContext>
{
    public PulseWatchDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PulseWatch")
            ?? "Host=localhost;Database=pulsewatch_design_time";

        var options = new DbContextOptionsBuilder<PulseWatchDbContext>();
        PersistenceSetup.Configure(options, connectionString);
        return new PulseWatchDbContext(options.Options);
    }
}

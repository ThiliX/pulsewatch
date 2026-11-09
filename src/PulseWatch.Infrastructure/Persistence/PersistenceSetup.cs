using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PulseWatch.Infrastructure.Persistence;

public static class PersistenceSetup
{
    public const string ConnectionStringName = "PulseWatch";

    /// <summary>Registers the PulseWatch DbContext. Fails fast with a clear message if the connection string is missing.</summary>
    public static IServiceCollection AddPulseWatchPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured. " +
                "For local development, set it with: dotnet user-secrets set \"ConnectionStrings:PulseWatch\" " +
                "\"Host=localhost;Port=5432;Database=pulsewatch;Username=pulsewatch;Password=<your password>\" " +
                "--project src/PulseWatch.Api (see README, 'Configuration and secrets'). " +
                "In other environments, set the ConnectionStrings__PulseWatch environment variable.");
        }

        services.AddDbContext<PulseWatchDbContext>(options => Configure(options, connectionString));
        return services;
    }

    /// <summary>Adds a readiness check: the database is reachable and has no pending migrations.</summary>
    public static IHealthChecksBuilder AddPulseWatchDatabaseCheck(this IHealthChecksBuilder builder, params string[] tags) =>
        builder.AddCheck<DatabaseReadyHealthCheck>(
            "database",
            failureStatus: HealthStatus.Unhealthy,
            tags: tags,
            timeout: TimeSpan.FromSeconds(5));

    /// <summary>
    /// Applies pending migrations. Only for local development (the API's --migrate switch) and tests;
    /// other environments use an EF Core migration bundle as a separate deployment step.
    /// </summary>
    public static async Task MigrateDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PulseWatchDbContext>();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Npgsql + snake_case naming. Shared by the app, design-time tooling and tests.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}

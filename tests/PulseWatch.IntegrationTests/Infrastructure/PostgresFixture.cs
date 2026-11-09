using Microsoft.EntityFrameworkCore;
using Npgsql;
using PulseWatch.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PulseWatch.IntegrationTests.Infrastructure.PostgresFixture))]

namespace PulseWatch.IntegrationTests.Infrastructure;

/// <summary>
/// One disposable PostgreSQL container for the whole test run (requires Docker).
/// Each test creates its own empty database, so tests never share data.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Same image as docker-compose.yml.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18.6-alpine").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates a new, empty database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync(bool migrate = true)
    {
        var name = $"test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = name,
        }.ConnectionString;

        if (migrate)
        {
            await using var db = CreateContext(connectionString);
            await db.Database.MigrateAsync();
        }

        return connectionString;
    }

    public static PulseWatchDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<PulseWatchDbContext>();
        PersistenceSetup.Configure(options, connectionString);
        return new PulseWatchDbContext(options.Options);
    }
}

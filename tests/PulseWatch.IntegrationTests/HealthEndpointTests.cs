using System.Net;
using PulseWatch.IntegrationTests.Infrastructure;

namespace PulseWatch.IntegrationTests;

public class HealthEndpointTests(PostgresFixture postgres)
{
    // Nothing listens on port 1, so connections are refused immediately.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ready_is_healthy_when_the_database_is_migrated()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Ready_is_unhealthy_when_migrations_are_pending()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync(migrate: false));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Ready_is_unhealthy_and_reveals_nothing_when_the_database_is_unreachable()
    {
        await using var factory = new ApiFactory(UnreachableDatabase);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Live_stays_healthy_when_the_database_is_unreachable()
    {
        await using var factory = new ApiFactory(UnreachableDatabase);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Unknown_route_returns_404()
    {
        await using var factory = new ApiFactory(UnreachableDatabase);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/does-not-exist", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Startup_fails_with_a_clear_message_when_the_connection_string_is_missing()
    {
        await using var factory = new ApiFactory(string.Empty);

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings:PulseWatch", ex.Message, StringComparison.Ordinal);
        Assert.Contains("user-secrets", ex.Message, StringComparison.Ordinal);
    }
}

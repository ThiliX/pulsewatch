using System.Net;
using System.Net.Http.Json;
using PulseWatch.IntegrationTests.Infrastructure;

namespace PulseWatch.IntegrationTests.Api;

public class RateLimitTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_attempts_are_limited_per_client()
    {
        await using var factory = new ApiFactory(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { ["RateLimiting:AuthPermitsPerMinute"] = "3" });
        using var client = await factory.CreateTestClientAsync();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var response = await client.Http.PostAsJsonAsync(
                "/api/v1/auth/login", new { email = "x@example.com", password = "wrong password!" }, Ct);
            statuses.Add(response.StatusCode);
            if (response.StatusCode == (HttpStatusCode)429)
            {
                Assert.NotNull(response.Headers.RetryAfter);
            }
        }

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, (HttpStatusCode)429], statuses);
    }

    [Fact]
    public async Task Monitor_writes_are_limited_per_user_not_globally()
    {
        await using var factory = new ApiFactory(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { ["RateLimiting:WritePermitsPerMinute"] = "2" });
        using var alice = await factory.CreateSignedInClientAsync();
        using var bob = await factory.CreateSignedInClientAsync();

        for (var i = 0; i < 2; i++)
        {
            using var ok = await alice.Http.PostAsJsonAsync("/api/v1/monitors", new { name = $"A{i}", url = $"https://a{i}.example.com/" }, Ct);
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        using var limited = await alice.Http.PostAsJsonAsync("/api/v1/monitors", new { name = "A3", url = "https://a3.example.com/" }, Ct);
        using var otherUser = await bob.Http.PostAsJsonAsync("/api/v1/monitors", new { name = "B", url = "https://b.example.com/" }, Ct);

        Assert.Equal((HttpStatusCode)429, limited.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherUser.StatusCode);
    }
}

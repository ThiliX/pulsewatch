using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PulseWatch.IntegrationTests.Infrastructure;

namespace PulseWatch.IntegrationTests.Api;

/// <summary>
/// The core isolation guarantee: user B can't see or change user A's monitors through ANY route,
/// and gets the same 404 as for an id that doesn't exist. Anonymous callers get 401 everywhere.
/// </summary>
public class OwnershipTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> MonitorRoutes => new()
    {
        { "GET", "/api/v1/monitors/{id}" },
        { "PATCH", "/api/v1/monitors/{id}" },
        { "DELETE", "/api/v1/monitors/{id}" },
        { "POST", "/api/v1/monitors/{id}/pause" },
        { "POST", "/api/v1/monitors/{id}/resume" },
        { "POST", "/api/v1/monitors/{id}/check-now" },
    };

    private static HttpRequestMessage Request(string method, string route, Guid id)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), route.Replace("{id}", id.ToString(), StringComparison.Ordinal));
        if (method == "PATCH")
        {
            request.Content = JsonContent.Create(new { name = "Hijacked" });
        }

        return request;
    }

    [Theory]
    [MemberData(nameof(MonitorRoutes))]
    public async Task Another_users_monitor_looks_exactly_like_a_missing_one(string method, string route)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new ApiFactory(connectionString);
        using var alice = await factory.CreateSignedInClientAsync();
        using var bob = await factory.CreateSignedInClientAsync();
        using var created = await alice.Http.PostAsJsonAsync(
            "/api/v1/monitors", new { name = "Alice's API", url = "https://alice.example.com/" }, Ct);
        var aliceMonitorId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        using var foreign = await bob.Http.SendAsync(Request(method, route, aliceMonitorId), Ct);
        using var missing = await bob.Http.SendAsync(Request(method, route, Guid.NewGuid()), Ct);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(
            (await missing.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString(),
            (await foreign.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString());

        // And nothing changed for Alice.
        await using var db = PostgresFixture.CreateContext(connectionString);
        var monitor = await db.Monitors.SingleAsync(m => m.Id == aliceMonitorId, Ct);
        Assert.Equal("Alice's API", monitor.Name);
        Assert.True(monitor.IsEnabled);
        Assert.Null(monitor.ManualCheckRequestedAtUtc);
    }

    [Fact]
    public async Task Listing_never_includes_other_users_monitors()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var alice = await factory.CreateSignedInClientAsync();
        using var bob = await factory.CreateSignedInClientAsync();
        (await alice.Http.PostAsJsonAsync("/api/v1/monitors", new { name = "Alice's API", url = "https://alice.example.com/" }, Ct)).Dispose();

        var bobsList = await bob.Http.GetFromJsonAsync<JsonElement>("/api/v1/monitors", Ct);

        Assert.Equal(0, bobsList.GetProperty("totalCount").GetInt32());
        Assert.Empty(bobsList.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(MonitorRoutes))]
    [InlineData("GET", "/api/v1/monitors")]
    [InlineData("POST", "/api/v1/monitors")]
    public async Task Anonymous_callers_get_401_on_every_monitor_route(string method, string route)
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var anonymous = await factory.CreateTestClientAsync();

        using var request = Request(method, route, Guid.NewGuid());
        if (method == "POST" && route == "/api/v1/monitors")
        {
            request.Content = JsonContent.Create(new { name = "API", url = "https://api.example.com/" });
        }

        using var response = await anonymous.Http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

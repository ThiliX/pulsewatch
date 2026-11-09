using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PulseWatch.Domain.Monitoring;
using PulseWatch.IntegrationTests.Infrastructure;

namespace PulseWatch.IntegrationTests.Api;

public class MonitorEndpointTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string _connectionString = string.Empty;

    private async Task<ApiFactory> CreateFactoryAsync(IReadOnlyDictionary<string, string?>? settings = null)
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        return new ApiFactory(_connectionString, settings);
    }

    private static async Task<JsonElement> CreateMonitorAsync(TestClient client, object body)
    {
        using var response = await client.Http.PostAsJsonAsync("/api/v1/monitors", body, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<JsonElement> ErrorsOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");

    [Fact]
    public async Task Create_applies_defaults_and_returns_201_with_location_and_etag()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateSignedInClientAsync();

        using var response = await client.Http.PostAsJsonAsync(
            "/api/v1/monitors", new { name = "  Orders API ", url = "https://API.example.com/health" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/monitors/{id}", response.Headers.Location?.OriginalString);
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal("Orders API", body.GetProperty("name").GetString());
        Assert.Equal("https://api.example.com/health", body.GetProperty("url").GetString());
        Assert.Equal("Unknown", body.GetProperty("state").GetString());
        Assert.Equal(60, body.GetProperty("intervalSeconds").GetInt32());
        Assert.Equal(10000, body.GetProperty("timeoutMs").GetInt32());
        Assert.Equal(3, body.GetProperty("failureThreshold").GetInt32());
        Assert.Equal(200, body.GetProperty("expectedStatusMin").GetInt32());
        Assert.Equal(299, body.GetProperty("expectedStatusMax").GetInt32());
        Assert.True(body.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task Create_reports_every_invalid_field()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateSignedInClientAsync();

        using var response = await client.Http.PostAsJsonAsync("/api/v1/monitors", new
        {
            name = "",
            url = "ftp://example.com",
            intervalSeconds = 10,
            timeoutMs = 999_999,
            failureThreshold = 0,
            recoveryThreshold = 11,
            expectedStatusMin = 300,
            expectedStatusMax = 200,
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ErrorsOf(response);
        foreach (var field in new[] { "name", "url", "intervalSeconds", "timeoutMs", "failureThreshold", "recoveryThreshold", "expectedStatusMax" })
        {
            Assert.True(errors.TryGetProperty(field, out _), $"Expected an error for '{field}'.");
        }
    }

    [Theory]
    [InlineData("http://localhost:8080/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://[::1]/")]
    [InlineData("https://api.private.example.com/")] // fake DNS answers include 10.0.0.1
    [InlineData("https://user:secret@api.example.com/")]
    [InlineData("https://api.example.com:22/")]
    public async Task Create_rejects_unsafe_destinations(string url)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateSignedInClientAsync();

        using var response = await client.Http.PostAsJsonAsync("/api/v1/monitors", new { name = "Bad", url }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ErrorsOf(response)).TryGetProperty("url", out _));
        await using var db = PostgresFixture.CreateContext(_connectionString);
        Assert.Equal(0, await db.Monitors.CountAsync(Ct));
    }

    [Fact]
    public async Task Create_enforces_the_per_user_quota()
    {
        await using var factory = await CreateFactoryAsync(new Dictionary<string, string?> { ["Monitoring:MaxMonitorsPerUser"] = "2" });
        using var alice = await factory.CreateSignedInClientAsync();
        using var bob = await factory.CreateSignedInClientAsync();
        await CreateMonitorAsync(alice, new { name = "One", url = "https://one.example.com/" });
        await CreateMonitorAsync(alice, new { name = "Two", url = "https://two.example.com/" });

        using var third = await alice.Http.PostAsJsonAsync("/api/v1/monitors", new { name = "Three", url = "https://three.example.com/" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);

        // Quotas are per user.
        await CreateMonitorAsync(bob, new { name = "Bob's", url = "https://bob.example.com/" });
    }

    [Fact]
    public async Task List_is_paged_newest_first_filters_by_state_and_shows_only_my_monitors()
    {
        await using var factory = await CreateFactoryAsync();
        using var alice = await factory.CreateSignedInClientAsync();
        using var bob = await factory.CreateSignedInClientAsync();
        for (var i = 1; i <= 3; i++)
        {
            await CreateMonitorAsync(alice, new { name = $"A{i}", url = $"https://a{i}.example.com/" });
        }

        await CreateMonitorAsync(bob, new { name = "B1", url = "https://b1.example.com/" });

        var page1 = await alice.Http.GetFromJsonAsync<JsonElement>("/api/v1/monitors?page=1&pageSize=2", Ct);
        var page2 = await alice.Http.GetFromJsonAsync<JsonElement>("/api/v1/monitors?page=2&pageSize=2", Ct);

        Assert.Equal(3, page1.GetProperty("totalCount").GetInt32());
        Assert.Equal(["A3", "A2"], page1.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("name").GetString()));
        Assert.Equal(["A1"], page2.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("name").GetString()));

        var paused = page1.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await alice.Http.PostAsync($"/api/v1/monitors/{paused}/pause", null, Ct)).Dispose();
        var pausedOnly = await alice.Http.GetFromJsonAsync<JsonElement>("/api/v1/monitors?state=paused", Ct);
        Assert.Equal(1, pausedOnly.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?state=Exploded")]
    [InlineData("?state=3")]
    public async Task List_rejects_invalid_paging_and_filters(string query)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateSignedInClientAsync();

        using var response = await client.Http.GetAsync($"/api/v1/monitors{query}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_changes_only_the_given_fields()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateSignedInClientAsync();
        var created = await CreateMonitorAsync(client, new { name = "Old", url = "https://api.example.com/", failureThreshold = 5 });
        var id = created.GetProperty("id").GetGuid();

        using var response = await client.Http.PatchAsJsonAsync($"/api/v1/monitors/{id}", new { name = "New", intervalSeconds = 300 }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("New", body.GetProperty("name").GetString());
        Assert.Equal(300, body.GetProperty("intervalSeconds").GetInt32());
        Assert.Equal(5, body.GetProperty("failureThreshold").GetInt32());
        Assert.Equal("https://api.example.com/", body.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Patch_rejects_an_unsafe_new_url()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateSignedInClientAsync();
        var id = (await CreateMonitorAsync(client, new { name = "API", url = "https://api.example.com/" })).GetProperty("id").GetGuid();

        using var response = await client.Http.PatchAsJsonAsync($"/api/v1/monitors/{id}", new { url = "http://127.0.0.1/" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var current = await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/monitors/{id}", Ct);
        Assert.Equal("https://api.example.com/", current.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Patch_with_a_stale_if_match_returns_412_and_changes_nothing()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateSignedInClientAsync();
        using var createResponse = await client.Http.PostAsJsonAsync("/api/v1/monitors", new { name = "V1", url = "https://api.example.com/" }, Ct);
        var staleETag = createResponse.Headers.ETag!.Tag;
        var id = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        (await client.Http.PatchAsJsonAsync($"/api/v1/monitors/{id}", new { name = "V2" }, Ct)).Dispose();

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/monitors/{id}") { Content = JsonContent.Create(new { name = "V3" }) };
        request.Headers.TryAddWithoutValidation("If-Match", staleETag);
        using var response = await client.Http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        var current = await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/monitors/{id}", Ct);
        Assert.Equal("V2", current.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Patch_with_the_current_if_match_succeeds()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateSignedInClientAsync();
        var id = (await CreateMonitorAsync(client, new { name = "V1", url = "https://api.example.com/" })).GetProperty("id").GetGuid();
        using var get = await client.Http.GetAsync($"/api/v1/monitors/{id}", Ct);

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/monitors/{id}") { Content = JsonContent.Create(new { name = "V2" }) };
        request.Headers.TryAddWithoutValidation("If-Match", get.Headers.ETag!.Tag);
        using var response = await client.Http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(get.Headers.ETag.Tag, response.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Pause_and_resume_change_state_and_are_idempotent()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateSignedInClientAsync();
        var id = (await CreateMonitorAsync(client, new { name = "API", url = "https://api.example.com/" })).GetProperty("id").GetGuid();

        using var pause1 = await client.Http.PostAsync($"/api/v1/monitors/{id}/pause", null, Ct);
        using var pause2 = await client.Http.PostAsync($"/api/v1/monitors/{id}/pause", null, Ct);
        var paused = await pause2.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(HttpStatusCode.OK, pause1.StatusCode);
        Assert.Equal("Paused", paused.GetProperty("state").GetString());
        Assert.False(paused.GetProperty("isEnabled").GetBoolean());

        using var resume = await client.Http.PostAsync($"/api/v1/monitors/{id}/resume", null, Ct);
        var resumed = await resume.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Unknown", resumed.GetProperty("state").GetString());
        Assert.True(resumed.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task Check_now_is_accepted_once_per_minute_and_refused_while_paused()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateSignedInClientAsync();
        var id = (await CreateMonitorAsync(client, new { name = "API", url = "https://api.example.com/" })).GetProperty("id").GetGuid();

        using var first = await client.Http.PostAsync($"/api/v1/monitors/{id}/check-now", null, Ct);
        using var second = await client.Http.PostAsync($"/api/v1/monitors/{id}/check-now", null, Ct);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal((HttpStatusCode)429, second.StatusCode);
        Assert.True(second.Headers.RetryAfter?.Delta > TimeSpan.Zero);

        (await client.Http.PostAsync($"/api/v1/monitors/{id}/pause", null, Ct)).Dispose();
        using var whilePaused = await client.Http.PostAsync($"/api/v1/monitors/{id}/check-now", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, whilePaused.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_the_monitor_and_its_history()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateSignedInClientAsync();
        var id = (await CreateMonitorAsync(client, new { name = "API", url = "https://api.example.com/" })).GetProperty("id").GetGuid();
        await using (var db = PostgresFixture.CreateContext(_connectionString))
        {
            var now = DateTimeOffset.UtcNow;
            db.MonitorChecks.Add(MonitorCheck.Create(id, now, now, 5, CheckOutcome.Success, 200, null));
            await db.SaveChangesAsync(Ct);
        }

        using var delete = await client.Http.DeleteAsync($"/api/v1/monitors/{id}", Ct);
        using var getAfter = await client.Http.GetAsync($"/api/v1/monitors/{id}", Ct);
        using var deleteAgain = await client.Http.DeleteAsync($"/api/v1/monitors/{id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getAfter.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
        await using var check = PostgresFixture.CreateContext(_connectionString);
        Assert.Equal(0, await check.MonitorChecks.CountAsync(Ct));
    }

    [Fact]
    public async Task Malformed_json_returns_400_without_internal_details()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateSignedInClientAsync();

        using var content = new StringContent("{ \"name\": ", System.Text.Encoding.UTF8, "application/json");
        using var response = await client.Http.PostAsync("/api/v1/monitors", content, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("   at ", text, StringComparison.Ordinal); // no stack trace
        Assert.DoesNotContain("Npgsql", text, StringComparison.Ordinal);
    }
}

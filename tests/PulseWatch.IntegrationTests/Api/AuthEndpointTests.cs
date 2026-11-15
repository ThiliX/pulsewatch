using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PulseWatch.IntegrationTests.Infrastructure;

namespace PulseWatch.IntegrationTests.Api;

public class AuthEndpointTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ApiFactory> CreateFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    [Fact]
    public async Task Register_signs_in_with_an_httponly_strict_cookie()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateTestClientAsync();

        using var register = await client.Http.PostAsJsonAsync(
            "/api/v1/auth/register", new { email = "Alice@Example.com", password = TestClient.Password }, Ct);

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var authCookie = Assert.Single(register.Headers.GetValues("Set-Cookie"), c => c.StartsWith("pw.auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", authCookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.Http.GetFromJsonAsync<JsonElement>("/api/v1/auth/me", Ct);
        Assert.Equal("Alice@Example.com", me.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Register_rejects_short_passwords_and_bad_emails_with_field_errors()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateTestClientAsync();

        using var response = await client.Http.PostAsJsonAsync(
            "/api/v1/auth/register", new { email = "not-an-email", password = "short" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("email", out _));

        using var weak = await client.Http.PostAsJsonAsync(
            "/api/v1/auth/register", new { email = "bob@example.com", password = "short" }, Ct);
        var weakErrors = (await weak.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        Assert.True(weakErrors.TryGetProperty("password", out _));
    }

    [Fact]
    public async Task Register_is_refused_when_registration_is_closed_but_existing_users_can_sign_in()
    {
        var database = await postgres.CreateDatabaseAsync();
        await using (var open = new ApiFactory(database))
        {
            using var existing = await open.CreateSignedInClientAsync("owner@example.com");
        }

        await using var closed = new ApiFactory(database, new Dictionary<string, string?> { ["Registration:Enabled"] = "false" });
        using var client = await closed.CreateTestClientAsync();

        using var register = await client.Http.PostAsJsonAsync(
            "/api/v1/auth/register", new { email = "stranger@example.com", password = TestClient.Password }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, register.StatusCode);
        var problem = await register.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Contains("Registration is closed", problem.GetProperty("title").GetString(), StringComparison.Ordinal);

        using var login = await client.Http.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = "owner@example.com", password = TestClient.Password }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Register_with_an_existing_email_fails_without_creating_a_second_account()
    {
        await using var factory = await CreateFactoryAsync();
        using var first = await factory.CreateSignedInClientAsync("taken@example.com");
        using var client = await factory.CreateTestClientAsync();

        using var response = await client.Http.PostAsJsonAsync(
            "/api/v1/auth/register", new { email = "TAKEN@example.com", password = TestClient.Password }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var me = await client.Http.GetAsync("/api/v1/auth/me", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_password_or_unknown_email_gives_the_same_401()
    {
        await using var factory = await CreateFactoryAsync();
        using var user = await factory.CreateSignedInClientAsync("carol@example.com");
        using var client = await factory.CreateTestClientAsync();

        using var wrongPassword = await client.Http.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = "carol@example.com", password = "wrong password here" }, Ct);
        using var unknownEmail = await client.Http.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = "nobody@example.com", password = "wrong password here" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(
            (await wrongPassword.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString(),
            (await unknownEmail.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Login_then_logout()
    {
        await using var factory = await CreateFactoryAsync();
        using var registered = await factory.CreateSignedInClientAsync("dave@example.com");
        using var client = await factory.CreateTestClientAsync();

        using var login = await client.Http.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = "DAVE@example.com", password = TestClient.Password }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var meWhileSignedIn = await client.Http.GetAsync("/api/v1/auth/me", Ct);
        Assert.Equal(HttpStatusCode.OK, meWhileSignedIn.StatusCode);

        using var logout = await client.Http.PostAsync("/api/v1/auth/logout", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var meAfter = await client.Http.GetAsync("/api/v1/auth/me", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, meAfter.StatusCode);
    }

    [Fact]
    public async Task Account_locks_after_five_failed_logins_even_for_the_right_password()
    {
        await using var factory = await CreateFactoryAsync();
        using var registered = await factory.CreateSignedInClientAsync("erin@example.com");
        using var client = await factory.CreateTestClientAsync();

        for (var i = 0; i < 5; i++)
        {
            using var failed = await client.Http.PostAsJsonAsync(
                "/api/v1/auth/login", new { email = "erin@example.com", password = "definitely wrong" }, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        using var locked = await client.Http.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = "erin@example.com", password = TestClient.Password }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_requests_get_401_problem_details_not_a_redirect()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateTestClientAsync();

        using var response = await client.Http.GetAsync("/api/v1/auth/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task State_changing_requests_without_the_xsrf_header_are_rejected()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = await factory.CreateSignedInClientAsync();
        client.Xsrf.Enabled = false;

        using var create = await client.Http.PostAsJsonAsync(
            "/api/v1/monitors", new { name = "API", url = "https://api.example.com/" }, Ct);
        using var logout = await client.Http.PostAsync("/api/v1/auth/logout", null, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.Contains("anti-forgery", await create.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Xsrf_token_from_another_session_is_rejected()
    {
        await using var factory = await CreateFactoryAsync();
        using var alice = await factory.CreateSignedInClientAsync();
        using var mallory = await factory.CreateSignedInClientAsync();
        alice.Xsrf.Enabled = false;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/monitors")
        {
            Content = JsonContent.Create(new { name = "API", url = "https://api.example.com/" }),
        };
        request.Headers.Add("X-XSRF-TOKEN", mallory.Xsrf.Token);
        using var response = await alice.Http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

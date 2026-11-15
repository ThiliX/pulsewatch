using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PulseWatch.IntegrationTests.Infrastructure;

namespace PulseWatch.IntegrationTests.Api;

/// <summary>Production hosting: security headers, serving the web app, and trusting the reverse proxy.</summary>
public sealed class HostingTests(PostgresFixture postgres) : IDisposable
{
    private readonly string _webRoot = CreateWebRoot();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/api/v1/auth/me")]    // 401
    [InlineData("/api/v1/nope")]       // 404
    [InlineData("/monitors")]          // web app
    public async Task Every_response_carries_the_security_headers(string path)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.True(response.Headers.Contains("Permissions-Policy"));
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Client_side_routes_get_index_html_that_is_always_revalidated()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/monitors/0b7c9a8e-1f2d-4c5b-9a6e-3d2f1c0b9a8e", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("PulseWatch test shell", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    [Fact]
    public async Task Hashed_assets_are_cached_for_a_year_and_missing_files_are_404()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();

        using var asset = await client.GetAsync("/assets/index-abc123.js", Ct);
        using var missing = await client.GetAsync("/assets/missing-file.js", Ct);

        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", asset.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/not-a-route")]
    [InlineData("/api/v2/monitors")]
    [InlineData("/health/unknown")]
    public async Task Unknown_api_urls_are_a_problem_details_404_not_the_web_app(string path)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_rate_limits_use_the_forwarded_client_address()
    {
        await using var factory = await CreateFactoryAsync(
            proxyAddress: "172.30.10.5",
            settings: new() { ["ReverseProxy:KnownNetworks:0"] = "172.30.10.0/24", ["RateLimiting:AuthPermitsPerMinute"] = "1" });
        using var test = await factory.CreateTestClientAsync();
        var client = test.Http;

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "203.0.113.1"));
        Assert.Equal((HttpStatusCode)429, await LoginFromAsync(client, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "203.0.113.2")); // a different user
    }

    [Fact]
    public async Task Forwarded_headers_from_an_untrusted_address_are_ignored()
    {
        await using var factory = await CreateFactoryAsync(
            proxyAddress: "198.51.100.9",
            settings: new() { ["ReverseProxy:KnownNetworks:0"] = "172.30.10.0/24", ["RateLimiting:AuthPermitsPerMinute"] = "1" });
        using var test = await factory.CreateTestClientAsync();
        var client = test.Http;

        // A client can't dodge the limit by inventing a new X-Forwarded-For value for every request.
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "203.0.113.1"));
        Assert.Equal((HttpStatusCode)429, await LoginFromAsync(client, "203.0.113.2"));
    }

    [Fact]
    public async Task An_invalid_proxy_network_stops_startup()
    {
        await using var factory = await CreateFactoryAsync(settings: new() { ["ReverseProxy:KnownNetworks:0"] = "not-a-network" });

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("not a CIDR range", error.ToString(), StringComparison.Ordinal);
    }

    private async Task<ApiFactory> CreateFactoryAsync(string? proxyAddress = null, Dictionary<string, string?>? settings = null)
    {
        settings ??= [];
        settings[WebHostDefaults.WebRootKey] = _webRoot;
        return new ApiFactory(
            await postgres.CreateDatabaseAsync(),
            settings,
            services =>
            {
                if (proxyAddress is not null)
                {
                    services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(IPAddress.Parse(proxyAddress)));
                }
            });
    }

    private static async Task<HttpStatusCode> LoginFromAsync(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email = "x@example.com", password = "wrong password!" }),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request, Ct);
        return response.StatusCode;
    }

    private static string CreateWebRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pulsewatch-webroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>PulseWatch test shell</title>");
        File.WriteAllText(Path.Combine(root, "assets", "index-abc123.js"), "console.log('app')");
        return root;
    }

    /// <summary>Makes every request look as if it came from <paramref name="address"/> (TestServer has no real socket).</summary>
    private sealed class RemoteAddressFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

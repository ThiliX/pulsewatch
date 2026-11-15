using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseWatch.Application.Security;

namespace PulseWatch.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real API in memory against the given database. DNS is faked so tests never touch the
/// network: host names containing "private" resolve to 10.0.0.1, everything else to a public address.
/// Rate limits are raised unless a test sets them, so they only trigger in the rate-limit tests.
/// </summary>
internal sealed class ApiFactory(
    string connectionString,
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:PulseWatch", connectionString);
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", "1000");
        builder.UseSetting("RateLimiting:WritePermitsPerMinute", "1000");
        builder.UseSetting("RateLimiting:CheckNowPermitsPerMinute", "1000");
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDnsResolver>();
            services.AddSingleton<IDnsResolver, FakeDns>();
            configureServices?.Invoke(services);
        });
    }

    /// <summary>A client with its own cookie jar that sends the anti-forgery header automatically.</summary>
    public async Task<TestClient> CreateTestClientAsync()
    {
        var xsrf = new XsrfHandler();
        var client = CreateDefaultClient(new Uri("http://localhost"), xsrf, new CookieContainerHandler());
        using var response = await client.GetAsync("/api/v1/auth/antiforgery");
        response.EnsureSuccessStatusCode();
        return new TestClient(client, xsrf);
    }

    /// <summary>Registers a new user and returns a signed-in client.</summary>
    public async Task<TestClient> CreateSignedInClientAsync(string? email = null)
    {
        var client = await CreateTestClientAsync();
        email ??= $"user-{Guid.NewGuid():N}@example.com";
        using var response = await client.Http.PostAsJsonAsync("/api/v1/auth/register", new { email, password = TestClient.Password });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        client.Email = email;
        return client;
    }

    private sealed class FakeDns : IDnsResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(
                host.Contains("private", StringComparison.OrdinalIgnoreCase)
                    ? [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("10.0.0.1")]
                    : [IPAddress.Parse("93.184.216.34")]);
    }
}

internal sealed class TestClient(HttpClient http, XsrfHandler xsrf) : IDisposable
{
    public const string Password = "correct horse battery staple";

    public HttpClient Http { get; } = http;

    public XsrfHandler Xsrf { get; } = xsrf;

    public string Email { get; set; } = string.Empty;

    public void Dispose() => Http.Dispose();
}

/// <summary>Remembers the latest XSRF-TOKEN cookie and copies it into the X-XSRF-TOKEN header, like the SPA will.</summary>
internal sealed class XsrfHandler : DelegatingHandler
{
    private const string CookiePrefix = "XSRF-TOKEN=";

    public string? Token { get; private set; }

    /// <summary>Set to false to send requests without the header.</summary>
    public bool Enabled { get; set; } = true;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Enabled && Token is not null && request.Method != HttpMethod.Get)
        {
            request.Headers.Remove("X-XSRF-TOKEN");
            request.Headers.Add("X-XSRF-TOKEN", Token);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies.Where(c => c.StartsWith(CookiePrefix, StringComparison.Ordinal)))
            {
                Token = Uri.UnescapeDataString(cookie[CookiePrefix.Length..].Split(';')[0]);
            }
        }

        return response;
    }
}

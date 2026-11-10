namespace PulseWatch.Application.Telemetry;

/// <summary>What may be written to logs about a monitored URL.</summary>
public static class LogRedaction
{
    /// <summary>
    /// Scheme and host (plus a non-default port) only, e.g. "https://api.example.com". Paths and query
    /// strings often carry tokens, so they never reach the logs.
    /// </summary>
    public static string UrlOrigin(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped)
            : "(invalid url)";
}

using System.Diagnostics.CodeAnalysis;

namespace PulseWatch.Domain.Common;

/// <summary>
/// Parses absolute URLs the same way on every operating system. On Linux and macOS,
/// <c>Uri.TryCreate("/path", UriKind.Absolute, …)</c> succeeds and returns <c>file:///path</c>, because a
/// leading slash looks like a Unix file path; on Windows it fails. PulseWatch only ever wants real URLs, so
/// such "implicit file paths" are treated as not absolute everywhere.
/// </summary>
public static class AbsoluteUrl
{
    public static bool TryParse(string? text, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(text)
            || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed)
            || IsImplicitFilePath(parsed))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>
    /// True for a URI that .NET made absolute only because the text looked like a file path
    /// ("/x" on Unix, "C:\x" on Windows), rather than because it said "file:".
    /// </summary>
    public static bool IsImplicitFilePath(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri
            && uri.IsFile
            && !uri.OriginalString.TrimStart().StartsWith("file:", StringComparison.OrdinalIgnoreCase);
    }
}

using PulseWatch.Domain.Common;

namespace PulseWatch.UnitTests.Domain;

/// <summary>
/// Runs in CI on Linux and locally on Windows. Uri.TryCreate behaves differently on the two for strings
/// that look like file paths; AbsoluteUrl must give the same answer on both.
/// </summary>
public class AbsoluteUrlTests
{
    [Theory]
    [InlineData("/relative")]          // file:///relative on Linux/macOS
    [InlineData("/etc/passwd")]
    [InlineData(@"C:\Windows\win.ini")] // file:///C:/... on Windows
    [InlineData("relative/path")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Paths_and_blanks_are_not_absolute_urls_on_any_os(string? text)
    {
        Assert.False(AbsoluteUrl.TryParse(text, out var uri));
        Assert.Null(uri);
    }

    [Theory]
    [InlineData("https://api.example.com/health", "https")]
    [InlineData("  http://api.example.com:8080/  ", "http")]
    [InlineData("file:///etc/passwd", "file")] // an explicit file: URL parses; callers reject the scheme
    [InlineData("ftp://example.com/", "ftp")]
    public void Real_urls_parse(string text, string scheme)
    {
        Assert.True(AbsoluteUrl.TryParse(text, out var uri));
        Assert.Equal(scheme, uri.Scheme);
    }
}

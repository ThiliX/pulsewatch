using System.ComponentModel.DataAnnotations;
using PulseWatch.Application.Options;

namespace PulseWatch.UnitTests.Application;

public class NotificationOptionsTests
{
    private static List<ValidationResult> Validate(object options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Defaults_are_valid_and_use_the_logging_provider()
    {
        var options = new NotificationOptions();

        Assert.Empty(Validate(options));
        Assert.Equal(EmailProvider.Logging, options.Provider);
        Assert.Equal(6, options.MaxAttempts);
        Assert.Empty(Validate(new EmailOptions()));
        Assert.Empty(Validate(new AppOptions()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Max_attempts_must_be_sensible(int attempts) =>
        Assert.NotEmpty(Validate(new NotificationOptions { MaxAttempts = attempts }));

    [Fact]
    public void From_address_must_be_an_email_address() =>
        Assert.NotEmpty(Validate(new EmailOptions { FromAddress = "not-an-address" }));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_smtp_settings_from_an_env_file_count_as_not_set(string blank)
    {
        var options = new EmailOptions { Host = blank, Username = blank, Password = blank, FromAddress = blank };

        Assert.Empty(Validate(options));
        Assert.Null(options.Host);
        Assert.Null(options.Username);
        Assert.Null(options.Password);
        Assert.Null(options.FromAddress);
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("/relative")]
    [InlineData("https://example.com/?token=abc")]
    [InlineData("https://example.com/#x")]
    public void Public_base_url_must_be_a_plain_absolute_http_url(string url) =>
        Assert.Contains(Validate(new AppOptions { PublicBaseUrl = url }),
            r => r.MemberNames.Contains(nameof(AppOptions.PublicBaseUrl)));

    [Theory]
    [InlineData("https://pulse.example.com", "https://pulse.example.com/")]
    [InlineData("https://example.com/pulse", "https://example.com/pulse/")]
    [InlineData("https://example.com/pulse/", "https://example.com/pulse/")]
    public void Public_base_uri_always_ends_with_a_slash(string url, string expected) =>
        Assert.Equal(expected, new AppOptions { PublicBaseUrl = url }.GetPublicBaseUri().AbsoluteUri);
}

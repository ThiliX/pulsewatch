using PulseWatch.Application.Monitors;
using PulseWatch.Application.Options;
using PulseWatch.Application.Security;
using PulseWatch.UnitTests.Domain;
using PulseWatch.UnitTests.Security;

namespace PulseWatch.UnitTests.Application;

public class MonitorRequestValidatorTests
{
    private readonly FakeDnsResolver _dns = new FakeDnsResolver()
        .Answer("api.example.com", "93.184.216.34")
        .Answer("new.example.com", "93.184.216.34");

    private MonitorRequestValidator CreateValidator(MonitoringOptions? options = null)
    {
        options ??= new MonitoringOptions();
        return new MonitorRequestValidator(new DestinationValidator(_dns, options.AllowedPorts), options);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_fills_in_defaults_from_options()
    {
        var validator = CreateValidator(new MonitoringOptions { DefaultIntervalSeconds = 120 });

        var result = await validator.ValidateCreateAsync(new CreateMonitorRequest("API", "https://api.example.com/"), Ct);

        Assert.True(result.IsValid);
        Assert.Equal(120, result.Input!.Settings.IntervalSeconds);
        Assert.Equal(10000, result.Input.Settings.TimeoutMs);
        Assert.Equal(3, result.Input.Settings.FailureThreshold);
    }

    [Fact]
    public async Task Operator_options_can_be_stricter_than_the_hard_limits()
    {
        var validator = CreateValidator(new MonitoringOptions { MinimumIntervalSeconds = 300, DefaultIntervalSeconds = 300, MaxTimeoutMs = 5000 });

        var result = await validator.ValidateCreateAsync(
            new CreateMonitorRequest("API", "https://api.example.com/", IntervalSeconds: 120, TimeoutMs: 8000), Ct);

        Assert.False(result.IsValid);
        Assert.Contains("intervalSeconds", result.Errors.Keys);
        Assert.Contains("timeoutMs", result.Errors.Keys);
    }

    [Fact]
    public async Task Default_timeout_is_capped_by_the_operator_maximum()
    {
        var validator = CreateValidator(new MonitoringOptions { MaxTimeoutMs = 5000 });

        var result = await validator.ValidateCreateAsync(new CreateMonitorRequest("API", "https://api.example.com/"), Ct);

        Assert.Equal(5000, result.Input!.Settings.TimeoutMs);
    }

    [Theory]
    [InlineData("Line\nbreak")]
    [InlineData("Tab\there")]
    public async Task Names_with_control_characters_are_rejected(string name)
    {
        var result = await CreateValidator().ValidateCreateAsync(new CreateMonitorRequest(name, "https://api.example.com/"), Ct);

        Assert.Contains("name", result.Errors.Keys);
    }

    [Fact]
    public async Task Update_without_a_url_change_does_not_query_dns()
    {
        var monitor = new MonitorTestData().Monitor;

        var result = await CreateValidator().ValidateUpdateAsync(new UpdateMonitorRequest(Name: "Renamed"), monitor, Ct);

        Assert.True(result.IsValid);
        Assert.Empty(_dns.Queries);
    }

    [Fact]
    public async Task Update_with_the_same_url_in_a_different_form_does_not_query_dns()
    {
        var monitor = new MonitorTestData().Monitor; // https://api.example.com/health

        var result = await CreateValidator().ValidateUpdateAsync(
            new UpdateMonitorRequest(Url: "https://API.example.com/health"), monitor, Ct);

        Assert.True(result.IsValid);
        Assert.Empty(_dns.Queries);
    }

    [Fact]
    public async Task Update_with_a_new_url_checks_the_destination()
    {
        var monitor = new MonitorTestData().Monitor;

        var ok = await CreateValidator().ValidateUpdateAsync(new UpdateMonitorRequest(Url: "https://new.example.com/"), monitor, Ct);
        var blocked = await CreateValidator().ValidateUpdateAsync(new UpdateMonitorRequest(Url: "http://localhost/"), monitor, Ct);

        Assert.True(ok.IsValid);
        Assert.Contains("new.example.com", _dns.Queries);
        Assert.Contains("url", blocked.Errors.Keys);
    }

    [Fact]
    public async Task Update_validates_the_merged_result_not_just_the_changed_fields()
    {
        var monitor = new MonitorTestData().Monitor; // expected status 200-299

        var result = await CreateValidator().ValidateUpdateAsync(new UpdateMonitorRequest(ExpectedStatusMin: 400), monitor, Ct);

        Assert.Contains("expectedStatusMax", result.Errors.Keys);
    }
}

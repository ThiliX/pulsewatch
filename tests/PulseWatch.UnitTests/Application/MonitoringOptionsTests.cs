using System.ComponentModel.DataAnnotations;
using PulseWatch.Application.Options;

namespace PulseWatch.UnitTests.Application;

public class MonitoringOptionsTests
{
    private static List<ValidationResult> Validate(MonitoringOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Defaults_are_valid() => Assert.Empty(Validate(new MonitoringOptions()));

    [Fact]
    public void Options_cannot_widen_the_hard_interval_limits()
    {
        var results = Validate(new MonitoringOptions { MinimumIntervalSeconds = 10 });

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(MonitoringOptions.MinimumIntervalSeconds)));
    }

    [Fact]
    public void Minimum_interval_must_not_exceed_maximum()
    {
        var results = Validate(new MonitoringOptions { MinimumIntervalSeconds = 600, MaximumIntervalSeconds = 300, DefaultIntervalSeconds = 300 });

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(MonitoringOptions.MaximumIntervalSeconds)));
    }

    [Fact]
    public void Default_interval_must_sit_between_minimum_and_maximum()
    {
        var results = Validate(new MonitoringOptions { MinimumIntervalSeconds = 120, DefaultIntervalSeconds = 60 });

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(MonitoringOptions.DefaultIntervalSeconds)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void Allowed_ports_must_be_valid_port_numbers(int port)
    {
        var results = Validate(new MonitoringOptions { AllowedPorts = [443, port] });

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(MonitoringOptions.AllowedPorts)));
    }

    [Fact]
    public void At_least_one_port_must_be_allowed()
    {
        var results = Validate(new MonitoringOptions { AllowedPorts = [] });

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(MonitoringOptions.AllowedPorts)));
    }
}

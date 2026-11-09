using System.ComponentModel.DataAnnotations;
using PulseWatch.Domain.Monitoring;

namespace PulseWatch.Application.Options;

/// <summary>
/// Operator settings for monitoring (section "Monitoring"). They can only narrow the hard
/// limits in <see cref="MonitorLimits"/>; the database enforces those limits as well.
/// Validated at startup, so a bad value stops the app with a clear message.
/// </summary>
public sealed class MonitoringOptions : IValidatableObject
{
    public const string SectionName = "Monitoring";

    /// <summary>How often the worker looks for due monitors.</summary>
    [Range(1, 60)]
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Maximum monitors claimed per poll.</summary>
    [Range(1, 500)]
    public int BatchSize { get; set; } = 50;

    /// <summary>Maximum probes running at the same time in one worker.</summary>
    [Range(1, 200)]
    public int MaxConcurrentChecks { get; set; } = 10;

    [Range(MonitorLimits.MinIntervalSeconds, MonitorLimits.MaxIntervalSeconds)]
    public int DefaultIntervalSeconds { get; set; } = MonitorLimits.DefaultIntervalSeconds;

    [Range(MonitorLimits.MinIntervalSeconds, MonitorLimits.MaxIntervalSeconds)]
    public int MinimumIntervalSeconds { get; set; } = MonitorLimits.MinIntervalSeconds;

    [Range(MonitorLimits.MinIntervalSeconds, MonitorLimits.MaxIntervalSeconds)]
    public int MaximumIntervalSeconds { get; set; } = MonitorLimits.MaxIntervalSeconds;

    [Range(MonitorLimits.MinTimeoutMs, MonitorLimits.MaxTimeoutMs)]
    public int MaxTimeoutMs { get; set; } = MonitorLimits.MaxTimeoutMs;

    /// <summary>Response bytes read per probe before the rest is discarded.</summary>
    [Range(1024, 1024 * 1024)]
    public int MaxResponseBytes { get; set; } = 64 * 1024;

    [Range(0, 5)]
    public int MaxRedirects { get; set; } = 3;

    /// <summary>Destination ports a monitor URL may use.</summary>
    [MinLength(1)]
    public int[] AllowedPorts { get; set; } = [80, 443, 8080, 8443];

    [Range(1, 1000)]
    public int MaxMonitorsPerUser { get; set; } = 20;

    [Range(1, 365)]
    public int CheckRetentionDays { get; set; } = 30;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinimumIntervalSeconds > MaximumIntervalSeconds)
        {
            yield return new ValidationResult(
                $"{nameof(MinimumIntervalSeconds)} must not be greater than {nameof(MaximumIntervalSeconds)}.",
                [nameof(MinimumIntervalSeconds), nameof(MaximumIntervalSeconds)]);
        }

        if (DefaultIntervalSeconds < MinimumIntervalSeconds || DefaultIntervalSeconds > MaximumIntervalSeconds)
        {
            yield return new ValidationResult(
                $"{nameof(DefaultIntervalSeconds)} must be between {nameof(MinimumIntervalSeconds)} and {nameof(MaximumIntervalSeconds)}.",
                [nameof(DefaultIntervalSeconds)]);
        }

        if (MaxTimeoutMs >= MinimumIntervalSeconds * 1000)
        {
            yield return new ValidationResult(
                $"{nameof(MaxTimeoutMs)} must be shorter than {nameof(MinimumIntervalSeconds)}.",
                [nameof(MaxTimeoutMs)]);
        }

        if (AllowedPorts.Any(port => port is < 1 or > 65535))
        {
            yield return new ValidationResult(
                $"Every entry in {nameof(AllowedPorts)} must be between 1 and 65535.",
                [nameof(AllowedPorts)]);
        }
    }
}

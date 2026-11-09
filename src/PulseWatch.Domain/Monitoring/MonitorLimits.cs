namespace PulseWatch.Domain.Monitoring;

/// <summary>
/// Hard limits that the domain and the database CHECK constraints both enforce.
/// Configuration (MonitoringOptions) may only narrow these, never widen them.
/// </summary>
public static class MonitorLimits
{
    public const int NameMaxLength = 100;
    public const int UrlMaxLength = 2048;

    public const int MinIntervalSeconds = 60;
    public const int MaxIntervalSeconds = 3600;
    public const int DefaultIntervalSeconds = 60;

    public const int MinTimeoutMs = 1000;
    public const int MaxTimeoutMs = 15000;
    public const int DefaultTimeoutMs = 10000;

    public const int MinThreshold = 1;
    public const int MaxThreshold = 10;
    public const int DefaultFailureThreshold = 3;
    public const int DefaultRecoveryThreshold = 1;

    public const int MinStatusCode = 100;
    public const int MaxStatusCode = 599;
    public const int DefaultExpectedStatusMin = 200;
    public const int DefaultExpectedStatusMax = 299;

    public const int ErrorSummaryMaxLength = 200;
}

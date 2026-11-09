using PulseWatch.Application.Options;
using PulseWatch.Application.Security;
using PulseWatch.Domain.Monitoring;

namespace PulseWatch.Application.Monitors;

/// <summary>A create or update request that passed validation, with defaults filled in.</summary>
public sealed record ValidatedMonitorInput(string Name, string Url, MonitorSettings Settings);

public sealed record MonitorValidationResult(ValidatedMonitorInput? Input, IDictionary<string, string[]> Errors)
{
    public bool IsValid => Input is not null;
}

/// <summary>
/// Server-side validation for monitor requests. Errors are keyed by JSON field name so the UI can
/// show them next to the right input. Limits come from <see cref="MonitoringOptions"/>, which can
/// only be stricter than the domain's hard limits.
/// </summary>
public sealed class MonitorRequestValidator(IDestinationValidator destinations, MonitoringOptions options)
{
    public Task<MonitorValidationResult> ValidateCreateAsync(CreateMonitorRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var interval = request.IntervalSeconds ?? options.DefaultIntervalSeconds;
        var values = new Candidate(
            request.Name,
            request.Url,
            interval,
            request.TimeoutMs ?? DefaultTimeout(interval),
            request.FailureThreshold ?? MonitorLimits.DefaultFailureThreshold,
            request.RecoveryThreshold ?? MonitorLimits.DefaultRecoveryThreshold,
            request.ExpectedStatusMin ?? MonitorLimits.DefaultExpectedStatusMin,
            request.ExpectedStatusMax ?? MonitorLimits.DefaultExpectedStatusMax);
        return ValidateAsync(values, checkDestination: true, cancellationToken);
    }

    public Task<MonitorValidationResult> ValidateUpdateAsync(
        UpdateMonitorRequest request, EndpointMonitor current, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        var values = new Candidate(
            request.Name ?? current.Name,
            request.Url ?? current.Url,
            request.IntervalSeconds ?? current.IntervalSeconds,
            request.TimeoutMs ?? current.TimeoutMs,
            request.FailureThreshold ?? current.FailureThreshold,
            request.RecoveryThreshold ?? current.RecoveryThreshold,
            request.ExpectedStatusMin ?? current.ExpectedStatusMin,
            request.ExpectedStatusMax ?? current.ExpectedStatusMax);

        // Only re-check DNS when the URL changes, so a temporary DNS problem can't block renaming.
        var urlChanged = request.Url is not null && !SameUrl(request.Url, current.Url);
        return ValidateAsync(values, urlChanged, cancellationToken);
    }

    private int DefaultTimeout(int intervalSeconds) =>
        Math.Min(Math.Min(MonitorLimits.DefaultTimeoutMs, options.MaxTimeoutMs), (intervalSeconds * 1000) - 1);

    private async Task<MonitorValidationResult> ValidateAsync(Candidate c, bool checkDestination, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(string field, string message) => errors[field] = [message];

        var name = c.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            Add("name", "Name is required.");
        }
        else if (name.Length > MonitorLimits.NameMaxLength)
        {
            Add("name", $"Name must be at most {MonitorLimits.NameMaxLength} characters.");
        }
        else if (name.Any(char.IsControl))
        {
            Add("name", "Name must not contain control characters.");
        }

        if (c.IntervalSeconds < options.MinimumIntervalSeconds || c.IntervalSeconds > options.MaximumIntervalSeconds)
        {
            Add("intervalSeconds", $"Interval must be between {options.MinimumIntervalSeconds} and {options.MaximumIntervalSeconds} seconds.");
        }

        if (c.TimeoutMs < MonitorLimits.MinTimeoutMs || c.TimeoutMs > options.MaxTimeoutMs)
        {
            Add("timeoutMs", $"Timeout must be between {MonitorLimits.MinTimeoutMs} and {options.MaxTimeoutMs} ms.");
        }
        else if (c.TimeoutMs >= c.IntervalSeconds * 1000L)
        {
            Add("timeoutMs", "Timeout must be shorter than the check interval.");
        }

        if (c.FailureThreshold is < MonitorLimits.MinThreshold or > MonitorLimits.MaxThreshold)
        {
            Add("failureThreshold", $"Failure threshold must be between {MonitorLimits.MinThreshold} and {MonitorLimits.MaxThreshold}.");
        }

        if (c.RecoveryThreshold is < MonitorLimits.MinThreshold or > MonitorLimits.MaxThreshold)
        {
            Add("recoveryThreshold", $"Recovery threshold must be between {MonitorLimits.MinThreshold} and {MonitorLimits.MaxThreshold}.");
        }

        if (c.ExpectedStatusMin is < MonitorLimits.MinStatusCode or > MonitorLimits.MaxStatusCode)
        {
            Add("expectedStatusMin", $"Status codes must be between {MonitorLimits.MinStatusCode} and {MonitorLimits.MaxStatusCode}.");
        }

        if (c.ExpectedStatusMax is < MonitorLimits.MinStatusCode or > MonitorLimits.MaxStatusCode)
        {
            Add("expectedStatusMax", $"Status codes must be between {MonitorLimits.MinStatusCode} and {MonitorLimits.MaxStatusCode}.");
        }
        else if (c.ExpectedStatusMin > c.ExpectedStatusMax)
        {
            Add("expectedStatusMax", "The highest expected status must not be lower than the lowest.");
        }

        var url = await ValidateUrlAsync(c.Url, checkDestination, cancellationToken).ConfigureAwait(false);
        if (url.Error is not null)
        {
            Add("url", url.Error);
        }

        if (errors.Count > 0)
        {
            return new MonitorValidationResult(null, errors);
        }

        var settings = new MonitorSettings(
            c.IntervalSeconds, c.TimeoutMs, c.FailureThreshold, c.RecoveryThreshold, c.ExpectedStatusMin, c.ExpectedStatusMax);
        return new MonitorValidationResult(new ValidatedMonitorInput(name!, url.Normalized!, settings), errors);
    }

    private async Task<(string? Normalized, string? Error)> ValidateUrlAsync(
        string? url, bool checkDestination, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return (null, "URL is required.");
        }

        string normalized;
        try
        {
            normalized = MonitorUrl.Normalize(url);
        }
        catch (Domain.Common.DomainException ex)
        {
            return (null, ex.Message);
        }

        if (checkDestination)
        {
            var check = await destinations.ValidateAsync(url, cancellationToken).ConfigureAwait(false);
            if (!check.IsAllowed)
            {
                return (null, check.Message);
            }
        }

        return (normalized, null);
    }

    private static bool SameUrl(string requested, string current)
    {
        try
        {
            return string.Equals(MonitorUrl.Normalize(requested), current, StringComparison.Ordinal);
        }
        catch (Domain.Common.DomainException)
        {
            return false;
        }
    }

    private sealed record Candidate(
        string? Name,
        string? Url,
        int IntervalSeconds,
        int TimeoutMs,
        int FailureThreshold,
        int RecoveryThreshold,
        int ExpectedStatusMin,
        int ExpectedStatusMax);
}

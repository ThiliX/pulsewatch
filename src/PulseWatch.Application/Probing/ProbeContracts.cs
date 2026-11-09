using PulseWatch.Domain.Monitoring;

namespace PulseWatch.Application.Probing;

/// <summary>One probe to run: a single GET (plus validated redirects) with an overall timeout.</summary>
public sealed record ProbeRequest(Uri Url, int TimeoutMs, int ExpectedStatusMin, int ExpectedStatusMax);

/// <summary>The outcome of exactly one probe attempt. Never retried, so failures are not hidden.</summary>
public sealed record ProbeResult(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int DurationMs,
    CheckOutcome Outcome,
    int? HttpStatusCode,
    string? ErrorSummary);

public interface IProbeRunner
{
    /// <summary>
    /// Runs the probe. Failures of the monitored endpoint come back as a result, never as an exception;
    /// only cancellation of <paramref name="cancellationToken"/> throws.
    /// </summary>
    Task<ProbeResult> ProbeAsync(ProbeRequest request, CancellationToken cancellationToken);
}

/// <summary>Thrown at connect time when a destination resolves to an address the policy forbids.</summary>
public sealed class DestinationBlockedException : Exception
{
    public DestinationBlockedException()
        : base("The destination resolves to a blocked address.")
    {
    }

    public DestinationBlockedException(string message)
        : base(message)
    {
    }

    public DestinationBlockedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Thrown at connect time when the host name can't be resolved.</summary>
public sealed class DnsResolutionFailedException : Exception
{
    public DnsResolutionFailedException()
        : base("The host name could not be resolved.")
    {
    }

    public DnsResolutionFailedException(string message)
        : base(message)
    {
    }

    public DnsResolutionFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

namespace PulseWatch.Application.Notifications;

/// <summary>
/// Delay before the next delivery attempt: 30 s × 2^(attempts − 1), capped at 1 hour, with ±20 % jitter
/// so messages that failed together (an SMTP outage) don't all retry in the same second.
/// With the default 6 attempts the retries come after about 30 s, 1, 2, 4 and 8 minutes.
/// </summary>
public static class RetryBackoff
{
    public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromHours(1);
    public const double JitterFraction = 0.2;

    /// <param name="attemptsMade">Attempts already made, including the one that just failed (1 or more).</param>
    /// <param name="random01">A random number in [0, 1); pass a fixed value in tests.</param>
    public static TimeSpan For(int attemptsMade, double random01)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptsMade, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(random01, 0d);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(random01, 1d);

        // Cap the exponent first so large attempt counts can't overflow.
        var exponent = Math.Min(attemptsMade - 1, 20);
        var seconds = Math.Min(BaseDelay.TotalSeconds * Math.Pow(2, exponent), MaxDelay.TotalSeconds);
        var jitter = 1 - JitterFraction + (2 * JitterFraction * random01);
        return TimeSpan.FromSeconds(Math.Min(seconds * jitter, MaxDelay.TotalSeconds));
    }
}

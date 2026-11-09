using System.Runtime.CompilerServices;

namespace PulseWatch.Domain.Common;

internal static class Guard
{
    /// <summary>All persisted timestamps are UTC; reject anything with a non-zero offset.</summary>
    public static DateTimeOffset Utc(DateTimeOffset value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException($"{name} must be a UTC timestamp.");
        }

        return value;
    }

    public static string Text(string? value, int maxLength, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException($"{name} is required.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{name} must be at most {maxLength} characters.");
        }

        return trimmed;
    }

    public static int InRange(int value, int min, int max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < min || value > max)
        {
            throw new DomainException($"{name} must be between {min} and {max}.");
        }

        return value;
    }
}

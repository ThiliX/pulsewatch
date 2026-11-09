using PulseWatch.Application.Notifications;

namespace PulseWatch.UnitTests.Notifications;

public class RetryBackoffTests
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(5, 480)]
    [InlineData(7, 1920)]
    public void Without_jitter_the_delay_doubles_from_30_seconds(int attempts, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RetryBackoff.For(attempts, 0.5));

    [Theory]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(int.MaxValue)]
    public void The_delay_is_capped_at_one_hour_even_with_jitter(int attempts)
    {
        Assert.Equal(TimeSpan.FromHours(1), RetryBackoff.For(attempts, 0.999));
        Assert.True(RetryBackoff.For(attempts, 0) <= TimeSpan.FromHours(1));
    }

    [Fact]
    public void Jitter_stays_within_twenty_percent()
    {
        Assert.Equal(TimeSpan.FromSeconds(24), RetryBackoff.For(1, 0));
        Assert.True(RetryBackoff.For(1, 0.9999) < TimeSpan.FromSeconds(36));
    }

    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(1, -0.1)]
    [InlineData(1, 1.0)]
    public void Invalid_arguments_are_rejected(int attempts, double random01) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RetryBackoff.For(attempts, random01));
}

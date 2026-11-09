using PulseWatch.Application.Monitors;

namespace PulseWatch.UnitTests.Application;

public class StatsWindowTests
{
    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(0, 5, 0d)]
    [InlineData(5, 5, 100d)]
    [InlineData(1, 4, 25d)]
    public void Uptime_is_successes_over_completed_checks_and_null_without_checks(int successful, int total, double? expected) =>
        Assert.Equal(expected, UptimeWindowResponse.Percent(successful, total));

    [Fact]
    public void Uptime_rejects_impossible_counts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UptimeWindowResponse.Percent(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => UptimeWindowResponse.Percent(2, 1));
    }

    [Theory]
    [InlineData("24h", "24h")]
    [InlineData("7D", "7d")]
    [InlineData("30d", "30d")]
    [InlineData("1y", null)]
    [InlineData(null, null)]
    public void Windows_parse_case_insensitively(string? input, string? expected) =>
        Assert.Equal(expected, StatsWindow.Parse(input)?.Name);

    [Fact]
    public void Every_window_has_at_most_300_buckets_and_divides_evenly()
    {
        Assert.All(StatsWindow.All, w =>
        {
            Assert.Equal(0, w.Length.Ticks % w.BucketSize.Ticks);
            Assert.InRange(w.Length.Ticks / w.BucketSize.Ticks, 1, 300);
        });
    }
}

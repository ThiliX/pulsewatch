using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PulseWatch.Application.Monitors;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Infrastructure.Metrics;

/// <summary>
/// Uptime, p95 and chart queries. Raw SQL because they need PostgreSQL aggregates (FILTER,
/// percentile_cont, date ranges via generate_series). All use the (monitor_id, started_at_utc) index.
/// Callers must check ownership first.
/// </summary>
public sealed class MonitorMetricsQueries(PulseWatchDbContext db)
{
    private const short Success = (short)CheckOutcome.Success;

    public async Task<IReadOnlyList<UptimeWindowResponse>> GetUptimeAsync(
        Guid monitorId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        // One pass over the longest window; each shorter window is a FILTER on the same rows.
        var windows = StatsWindow.All;
        var columns = string.Join(",\n", windows.Select((_, i) => $"""
                count(*) FILTER (WHERE started_at_utc >= @from{i}),
                count(*) FILTER (WHERE started_at_utc >= @from{i} AND outcome = {Success}),
                percentile_cont(0.95) WITHIN GROUP (ORDER BY duration_ms) FILTER (WHERE started_at_utc >= @from{i} AND outcome = {Success})
            """));
        var sql = $"""
            SELECT
            {columns}
            FROM monitor_checks
            WHERE monitor_id = @monitor_id AND started_at_utc >= @from_all AND started_at_utc < @to
            """;

        return await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("monitor_id", monitorId));
            command.Parameters.Add(new NpgsqlParameter("to", nowUtc));
            command.Parameters.Add(new NpgsqlParameter("from_all", nowUtc - windows.Max(w => w.Length)));
            for (var i = 0; i < windows.Count; i++)
            {
                command.Parameters.Add(new NpgsqlParameter($"from{i}", nowUtc - windows[i].Length));
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            var result = new List<UptimeWindowResponse>(windows.Count);
            for (var i = 0; i < windows.Count; i++)
            {
                var total = (int)reader.GetInt64(i * 3);
                var successful = (int)reader.GetInt64((i * 3) + 1);
                int? p95 = reader.IsDBNull((i * 3) + 2) ? null : (int)Math.Round(reader.GetDouble((i * 3) + 2));
                result.Add(new UptimeWindowResponse(
                    windows[i].Name,
                    nowUtc - windows[i].Length,
                    nowUtc,
                    total,
                    successful,
                    UptimeWindowResponse.Percent(successful, total),
                    p95));
            }

            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CheckSeriesResponse> GetSeriesAsync(
        Guid monitorId, StatsWindow window, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        // Buckets are aligned to the bucket size (e.g. whole hours), so the chart doesn't shift every refresh.
        var bucketTicks = window.BucketSize.Ticks;
        var lastBucket = new DateTimeOffset(nowUtc.UtcTicks - (nowUtc.UtcTicks % bucketTicks), TimeSpan.Zero);
        var bucketCount = (int)(window.Length.Ticks / bucketTicks);
        var firstBucket = lastBucket - (window.BucketSize * (bucketCount - 1));

        // One pass over the window's checks: date_bin assigns each check to its bucket, then the full list
        // of buckets is joined on, so empty buckets appear too. (Joining buckets to checks by range instead
        // compares every bucket with every check, which grows to millions of comparisons on a 30-day window.)
        const string Sql = """
            WITH agg AS (
                SELECT date_bin(@stride, started_at_utc, @first) AS bucket,
                       count(*) AS checks,
                       count(*) FILTER (WHERE outcome <> @success) AS failures,
                       avg(duration_ms) FILTER (WHERE outcome = @success) AS avg_ms,
                       max(duration_ms) FILTER (WHERE outcome = @success) AS max_ms
                FROM monitor_checks
                WHERE monitor_id = @monitor_id AND started_at_utc >= @first AND started_at_utc < @last + @stride
                GROUP BY 1)
            SELECT b.bucket, coalesce(agg.checks, 0), coalesce(agg.failures, 0), agg.avg_ms, agg.max_ms
            FROM generate_series(@first, @last, @stride) AS b(bucket)
            LEFT JOIN agg ON agg.bucket = b.bucket
            ORDER BY b.bucket
            """;

        var points = await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Sql;
            command.Parameters.Add(new NpgsqlParameter("monitor_id", monitorId));
            command.Parameters.Add(new NpgsqlParameter("first", firstBucket));
            command.Parameters.Add(new NpgsqlParameter("last", lastBucket));
            command.Parameters.Add(new NpgsqlParameter("stride", window.BucketSize));
            command.Parameters.Add(new NpgsqlParameter<short>("success", Success));

            var list = new List<CheckSeriesPoint>(bucketCount);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(new CheckSeriesPoint(
                    reader.GetFieldValue<DateTimeOffset>(0),
                    (int)reader.GetInt64(1),
                    (int)reader.GetInt64(2),
                    reader.IsDBNull(3) ? null : (int)Math.Round(reader.GetDecimal(3)),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4)));
            }

            return list;
        }, cancellationToken).ConfigureAwait(false);

        return new CheckSeriesResponse(
            window.Name, (int)window.BucketSize.TotalSeconds, firstBucket, lastBucket + window.BucketSize, points);
    }

    private async Task<T> WithConnectionAsync<T>(
        Func<System.Data.Common.DbConnection, Task<T>> action, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await action(connection).ConfigureAwait(false);
        }
        finally
        {
            if (openedHere)
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }
}

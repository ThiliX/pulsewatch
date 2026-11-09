using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PulseWatch.Application.Scheduling;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Infrastructure.Scheduling;

/// <summary>
/// Leases due monitors with one atomic statement. FOR UPDATE SKIP LOCKED lets several workers claim
/// concurrently without blocking each other or receiving the same row; the lease (lease_until_utc)
/// keeps a monitor out of other workers' hands until its result is recorded or the lease expires
/// (e.g. the worker crashed mid-check).
/// </summary>
internal sealed class MonitorClaimer(PulseWatchDbContext db) : IMonitorClaimer
{
    private const string ClaimSql = """
        UPDATE monitors AS m
        SET lease_until_utc = @lease_until
        WHERE m.id IN (
            SELECT id FROM monitors
            WHERE is_enabled
              AND next_check_at_utc <= @now
              AND (lease_until_utc IS NULL OR lease_until_utc < @now)
            ORDER BY next_check_at_utc
            LIMIT @batch_size
            FOR UPDATE SKIP LOCKED)
        RETURNING m.id, m.url, m.timeout_ms, m.expected_status_min, m.expected_status_max
        """;

    public async Task<IReadOnlyList<ClaimedMonitor>> ClaimDueAsync(
        DateTimeOffset nowUtc, int batchSize, TimeSpan lease, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = ClaimSql;
            command.Parameters.Add(new NpgsqlParameter("now", nowUtc));
            command.Parameters.Add(new NpgsqlParameter("lease_until", nowUtc + lease));
            command.Parameters.Add(new NpgsqlParameter("batch_size", batchSize));

            var claimed = new List<ClaimedMonitor>(batchSize);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                claimed.Add(new ClaimedMonitor(
                    reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
            }

            return claimed;
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

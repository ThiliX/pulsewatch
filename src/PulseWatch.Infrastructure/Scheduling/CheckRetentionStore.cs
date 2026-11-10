using Microsoft.EntityFrameworkCore;
using PulseWatch.Application.Scheduling;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Infrastructure.Scheduling;

/// <summary>
/// One bounded DELETE per call. The inner SELECT uses the started_at_utc index, so each batch reads only
/// the rows it deletes instead of scanning the table.
/// </summary>
internal sealed class CheckRetentionStore(PulseWatchDbContext db) : ICheckRetentionStore
{
    public Task<int> DeleteChecksBeforeAsync(DateTimeOffset cutoffUtc, int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        return db.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM monitor_checks
            WHERE id IN (
                SELECT id FROM monitor_checks
                WHERE started_at_utc < {cutoffUtc}
                ORDER BY started_at_utc
                LIMIT {batchSize})
            """,
            cancellationToken);
    }
}

using Microsoft.EntityFrameworkCore;
using PulseWatch.Application.Scheduling;
using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Infrastructure.Scheduling;

internal sealed class WorkerHeartbeatStore(PulseWatchDbContext db) : IWorkerHeartbeatStore
{
    public async Task BeatAsync(string workerId, string version, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO worker_heartbeats (worker_id, version, started_at_utc, last_seen_at_utc)
            VALUES ({workerId}, {version}, {nowUtc}, {nowUtc})
            ON CONFLICT (worker_id) DO UPDATE
            SET last_seen_at_utc = GREATEST(worker_heartbeats.last_seen_at_utc, EXCLUDED.last_seen_at_utc),
                version = EXCLUDED.version
            """,
            cancellationToken).ConfigureAwait(false);
}

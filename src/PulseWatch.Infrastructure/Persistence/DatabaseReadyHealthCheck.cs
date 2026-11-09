using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PulseWatch.Infrastructure.Persistence;

/// <summary>
/// Ready = the database answers and the schema is up to date.
/// The health endpoint only prints Healthy/Unhealthy, so the descriptions here are never shown to callers.
/// </summary>
internal sealed class DatabaseReadyHealthCheck(PulseWatchDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                return new HealthCheckResult(context.Registration.FailureStatus, "Database is unreachable.");
            }

            var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
            return pending.Any()
                ? new HealthCheckResult(context.Registration.FailureStatus, "Database has pending migrations.")
                : HealthCheckResult.Healthy();
        }
        catch (DbException ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Database check failed.", ex);
        }
    }
}

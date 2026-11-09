using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PulseWatch.Domain.Incidents;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Domain.Notifications;
using PulseWatch.Domain.Workers;
using PulseWatch.Infrastructure.Identity;

namespace PulseWatch.Infrastructure.Persistence;

public sealed class PulseWatchDbContext(DbContextOptions<PulseWatchDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<EndpointMonitor> Monitors => Set<EndpointMonitor>();

    public DbSet<MonitorCheck> MonitorChecks => Set<MonitorCheck>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<NotificationOutboxMessage> NotificationOutbox => Set<NotificationOutboxMessage>();

    public DbSet<WorkerHeartbeat> WorkerHeartbeats => Set<WorkerHeartbeat>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity sets PascalCase table names explicitly; use snake_case like the rest of the schema.
        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<IdentityRole>().ToTable("roles");
        builder.Entity<IdentityUserRole<string>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<string>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("role_claims");

        builder.ApplyConfigurationsFromAssembly(typeof(PulseWatchDbContext).Assembly);
    }
}

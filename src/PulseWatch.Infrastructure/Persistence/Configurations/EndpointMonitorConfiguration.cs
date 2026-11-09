using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Infrastructure.Identity;
using L = PulseWatch.Domain.Monitoring.MonitorLimits;

namespace PulseWatch.Infrastructure.Persistence.Configurations;

internal sealed class EndpointMonitorConfiguration : IEntityTypeConfiguration<EndpointMonitor>
{
    public void Configure(EntityTypeBuilder<EndpointMonitor> builder)
    {
        // Column names in CHECK constraints are snake_case (UseSnakeCaseNamingConvention).
        builder.ToTable("monitors", t =>
        {
            t.HasCheckConstraint("ck_monitors_interval",
                $"interval_seconds BETWEEN {L.MinIntervalSeconds} AND {L.MaxIntervalSeconds}");
            t.HasCheckConstraint("ck_monitors_timeout",
                $"timeout_ms BETWEEN {L.MinTimeoutMs} AND {L.MaxTimeoutMs} AND timeout_ms < interval_seconds * 1000");
            t.HasCheckConstraint("ck_monitors_failure_threshold",
                $"failure_threshold BETWEEN {L.MinThreshold} AND {L.MaxThreshold}");
            t.HasCheckConstraint("ck_monitors_recovery_threshold",
                $"recovery_threshold BETWEEN {L.MinThreshold} AND {L.MaxThreshold}");
            t.HasCheckConstraint("ck_monitors_expected_status",
                $"expected_status_min BETWEEN {L.MinStatusCode} AND {L.MaxStatusCode} " +
                $"AND expected_status_max BETWEEN {L.MinStatusCode} AND {L.MaxStatusCode} " +
                "AND expected_status_min <= expected_status_max");
            t.HasCheckConstraint("ck_monitors_counters",
                "consecutive_failures >= 0 AND consecutive_successes >= 0");
        });

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.OwnerUserId).IsRequired();
        builder.Property(m => m.Name).HasMaxLength(L.NameMaxLength).IsRequired();
        builder.Property(m => m.Url).HasMaxLength(L.UrlMaxLength).IsRequired();

        // PostgreSQL's system column xmin changes on every row update: a free optimistic concurrency token.
        builder.Property(m => m.Version).IsRowVersion();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(m => m.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.OwnerUserId);

        // The scheduler's "what is due?" query only looks at enabled monitors.
        builder.HasIndex(m => m.NextCheckAtUtc)
            .HasFilter("is_enabled")
            .HasDatabaseName("ix_monitors_due");
    }
}

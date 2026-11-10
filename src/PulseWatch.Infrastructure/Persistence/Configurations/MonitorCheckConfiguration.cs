using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PulseWatch.Domain.Monitoring;

namespace PulseWatch.Infrastructure.Persistence.Configurations;

internal sealed class MonitorCheckConfiguration : IEntityTypeConfiguration<MonitorCheck>
{
    public void Configure(EntityTypeBuilder<MonitorCheck> builder)
    {
        builder.ToTable("monitor_checks", t =>
        {
            t.HasCheckConstraint("ck_monitor_checks_duration", "duration_ms >= 0");
            t.HasCheckConstraint("ck_monitor_checks_completed_after_started", "completed_at_utc >= started_at_utc");
            t.HasCheckConstraint("ck_monitor_checks_status_code",
                $"http_status_code IS NULL OR http_status_code BETWEEN {MonitorLimits.MinStatusCode} AND {MonitorLimits.MaxStatusCode}");
        });

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.ErrorSummary).HasMaxLength(MonitorLimits.ErrorSummaryMaxLength);

        builder.HasOne<EndpointMonitor>()
            .WithMany()
            .HasForeignKey(c => c.MonitorId)
            .OnDelete(DeleteBehavior.Cascade);

        // History is always read per monitor, newest first.
        builder.HasIndex(c => new { c.MonitorId, c.StartedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("ix_monitor_checks_monitor_started");

        // Retention deletes the oldest checks across all monitors in batches; without this index each
        // batch would scan the whole table.
        builder.HasIndex(c => c.StartedAtUtc)
            .HasDatabaseName("ix_monitor_checks_started_at");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PulseWatch.Domain.Incidents;
using PulseWatch.Domain.Monitoring;

namespace PulseWatch.Infrastructure.Persistence.Configurations;

internal sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public const string OneOpenIncidentPerMonitorIndex = "ux_incidents_one_open_per_monitor";

    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        var open = (short)IncidentStatus.Open;
        var resolved = (short)IncidentStatus.Resolved;

        builder.ToTable("incidents", t =>
        {
            t.HasCheckConstraint("ck_incidents_failure_count", "failure_count >= 1");
            t.HasCheckConstraint("ck_incidents_opened_after_first_failure", "opened_at_utc >= first_failure_at_utc");
            t.HasCheckConstraint("ck_incidents_resolution",
                $"(status = {open} AND resolved_at_utc IS NULL) OR " +
                $"(status = {resolved} AND resolved_at_utc IS NOT NULL AND resolved_at_utc >= opened_at_utc)");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.HasOne<EndpointMonitor>()
            .WithMany()
            .HasForeignKey(i => i.MonitorId)
            .OnDelete(DeleteBehavior.Cascade);

        // The database itself guarantees at most one open incident per monitor,
        // even if two writers race or the same check result is processed twice.
        builder.HasIndex(i => i.MonitorId)
            .IsUnique()
            .HasFilter($"status = {open}")
            .HasDatabaseName(OneOpenIncidentPerMonitorIndex);

        builder.HasIndex(i => new { i.MonitorId, i.OpenedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("ix_incidents_monitor_opened");
    }
}

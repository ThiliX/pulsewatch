using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PulseWatch.Domain.Workers;

namespace PulseWatch.Infrastructure.Persistence.Configurations;

internal sealed class WorkerHeartbeatConfiguration : IEntityTypeConfiguration<WorkerHeartbeat>
{
    public void Configure(EntityTypeBuilder<WorkerHeartbeat> builder)
    {
        builder.ToTable("worker_heartbeats");
        builder.HasKey(h => h.WorkerId);
        builder.Property(h => h.WorkerId).HasMaxLength(WorkerHeartbeat.WorkerIdMaxLength);
        builder.Property(h => h.Version).HasMaxLength(WorkerHeartbeat.VersionMaxLength).IsRequired();
    }
}

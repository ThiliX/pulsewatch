using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PulseWatch.Domain.Incidents;
using PulseWatch.Domain.Notifications;

namespace PulseWatch.Infrastructure.Persistence.Configurations;

internal sealed class NotificationOutboxConfiguration : IEntityTypeConfiguration<NotificationOutboxMessage>
{
    public void Configure(EntityTypeBuilder<NotificationOutboxMessage> builder)
    {
        builder.ToTable("notification_outbox", t =>
            t.HasCheckConstraint("ck_notification_outbox_attempts", "attempt_count >= 0"));

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Recipient).HasMaxLength(NotificationOutboxMessage.RecipientMaxLength).IsRequired();
        builder.Property(m => m.IdempotencyKey).HasMaxLength(NotificationOutboxMessage.IdempotencyKeyMaxLength).IsRequired();
        builder.Property(m => m.LastErrorCategory).HasMaxLength(NotificationOutboxMessage.ErrorCategoryMaxLength);

        builder.HasOne<Incident>()
            .WithMany()
            .HasForeignKey(m => m.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.IdempotencyKey).IsUnique();

        // The dispatcher only looks for messages that still need delivering.
        builder.HasIndex(m => m.NextAttemptAtUtc)
            .HasFilter($"status IN ({(short)NotificationStatus.Pending}, {(short)NotificationStatus.Failed})")
            .HasDatabaseName("ix_notification_outbox_due");

        builder.HasIndex(m => m.IncidentId);
    }
}

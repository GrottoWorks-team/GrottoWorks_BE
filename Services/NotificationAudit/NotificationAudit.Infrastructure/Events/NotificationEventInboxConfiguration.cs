using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NotificationAudit.Infrastructure.Events;

internal sealed class NotificationEventInboxConfiguration : IEntityTypeConfiguration<NotificationEventInbox>
{
    public void Configure(EntityTypeBuilder<NotificationEventInbox> builder)
    {
        builder.ToTable("notification_event_inbox");
        builder.HasKey(message => message.EventId);
        builder.Property(message => message.EventId).HasColumnName("event_id");
        builder.Property(message => message.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(message => message.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        builder.Property(message => message.ReceivedAt).HasColumnName("received_at");
        builder.Property(message => message.ProcessedAt).HasColumnName("processed_at");
        builder.Property(message => message.LastError).HasColumnName("last_error").HasMaxLength(2000);
        builder.HasIndex(message => new { message.Status, message.ReceivedAt })
            .HasDatabaseName("ix_notification_event_inbox_status_received_at");
    }
}

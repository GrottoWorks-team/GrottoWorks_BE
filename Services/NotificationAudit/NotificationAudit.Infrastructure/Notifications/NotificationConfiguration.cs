using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationAudit.Domain.Notifications;

namespace NotificationAudit.Infrastructure.Notifications;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notification");
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id).HasColumnName("notification_id");
        builder.Property(notification => notification.RecipientUserId).HasColumnName("recipient_user_id");
        builder.Property(notification => notification.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(notification => notification.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(notification => notification.Body).HasColumnName("body").IsRequired();
        builder.Property(notification => notification.ReferenceType).HasColumnName("reference_type").HasMaxLength(50);
        builder.Property(notification => notification.ReferenceId).HasColumnName("reference_id");
        builder.Property(notification => notification.IsRead).HasColumnName("is_read");
        builder.Property(notification => notification.CreatedAt).HasColumnName("created_at");
        builder.Property(notification => notification.ReadAt).HasColumnName("read_at");
        builder.HasIndex(notification => new { notification.RecipientUserId, notification.CreatedAt })
            .HasDatabaseName("ix_notification_recipient_created_at");
        builder.HasIndex(notification => new { notification.RecipientUserId, notification.IsRead })
            .HasDatabaseName("ix_notification_recipient_is_read");
    }
}

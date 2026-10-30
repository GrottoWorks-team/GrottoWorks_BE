using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationAudit.Domain.Notifications;

namespace NotificationAudit.Infrastructure.Notifications;

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preference");
        builder.HasKey(preference => preference.UserId);
        builder.Property(preference => preference.UserId).HasColumnName("user_id");
        builder.Property(preference => preference.InAppEnabled).HasColumnName("in_app_enabled").HasDefaultValue(true);
        builder.Property(preference => preference.PushEnabled).HasColumnName("push_enabled").HasDefaultValue(true);
        builder.Property(preference => preference.EmailEnabled).HasColumnName("email_enabled").HasDefaultValue(false);
        builder.Property(preference => preference.UpdatedAt).HasColumnName("updated_at");
    }
}

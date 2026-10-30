using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationAudit.Domain.Notifications;

namespace NotificationAudit.Infrastructure.Notifications;

internal sealed class NotificationRuleConfiguration : IEntityTypeConfiguration<NotificationRule>
{
    public void Configure(EntityTypeBuilder<NotificationRule> builder)
    {
        builder.ToTable("notification_rule");
        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.Id).HasColumnName("notification_rule_id");
        builder.Property(rule => rule.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.HasIndex(rule => rule.EventType).IsUnique().HasDatabaseName("ux_notification_rule_event_type");
        builder.Property(rule => rule.InAppEnabled).HasColumnName("in_app_enabled").HasDefaultValue(true);
        builder.Property(rule => rule.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
        builder.Property(rule => rule.UpdatedAt).HasColumnName("updated_at");
    }
}

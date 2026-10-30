using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationAudit.Domain.Audit;

namespace NotificationAudit.Infrastructure.Audit;

internal sealed class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.ToTable("activity_log");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.Id).HasColumnName("activity_log_id");
        builder.Property(log => log.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(log => log.ParishId).HasColumnName("parish_id");
        builder.Property(log => log.SeasonId).HasColumnName("season_id");
        builder.Property(log => log.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
        builder.Property(log => log.EntityType).HasColumnName("entity_type").HasMaxLength(50).IsRequired();
        builder.Property(log => log.EntityId).HasColumnName("entity_id");
        builder.Property(log => log.OldValue).HasColumnName("old_value").HasColumnType("jsonb");
        builder.Property(log => log.NewValue).HasColumnName("new_value").HasColumnType("jsonb");
        builder.Property(log => log.Reason).HasColumnName("reason");
        builder.Property(log => log.CorrelationId).HasColumnName("correlation_id");
        builder.Property(log => log.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(log => new { log.EntityType, log.EntityId, log.CreatedAt })
            .HasDatabaseName("ix_activity_log_entity_created_at");
        builder.HasIndex(log => new { log.ActorUserId, log.CreatedAt })
            .HasDatabaseName("ix_activity_log_actor_created_at");
    }
}

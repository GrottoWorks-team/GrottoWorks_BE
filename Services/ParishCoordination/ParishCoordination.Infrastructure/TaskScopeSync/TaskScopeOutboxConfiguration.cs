using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ParishCoordination.Infrastructure.TaskScopeSync;

public sealed class TaskScopeOutboxConfiguration : IEntityTypeConfiguration<TaskScopeOutbox>
{
    public void Configure(EntityTypeBuilder<TaskScopeOutbox> builder)
    {
        builder.ToTable("task_scope_outbox");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.ScopeType).HasColumnName("scope_type").HasMaxLength(20).IsRequired();
        builder.Property(row => row.AggregateId).HasColumnName("aggregate_id");
        builder.Property(row => row.SourceVersion).HasColumnName("source_version");
        builder.Property(row => row.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.CreatedAt).HasColumnName("created_at");
        builder.Property(row => row.Attempts).HasColumnName("attempts");
        builder.Property(row => row.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(row => row.DeliveredAt).HasColumnName("delivered_at");
        builder.Property(row => row.LastError).HasColumnName("last_error").HasMaxLength(500);
        builder.HasIndex(row => new { row.ScopeType, row.AggregateId, row.SourceVersion })
            .IsUnique().HasDatabaseName("ux_task_scope_outbox_version");
        builder.HasIndex(row => row.NextAttemptAt).HasFilter("delivered_at IS NULL")
            .HasDatabaseName("ix_task_scope_outbox_pending");
    }
}

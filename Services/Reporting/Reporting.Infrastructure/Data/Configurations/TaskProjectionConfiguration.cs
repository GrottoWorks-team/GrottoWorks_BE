using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Reporting.Domain.Scope;

namespace Reporting.Infrastructure.Data.Configurations;

/// <summary><c>task_projection</c> — local read-only copy of TaskExecution tasks (spec §2.2, not in the logical ERD).</summary>
public sealed class TaskProjectionConfiguration : IEntityTypeConfiguration<TaskProjection>
{
    public void Configure(EntityTypeBuilder<TaskProjection> builder)
    {
        builder.ToTable("task_projection");
        builder.HasKey(task => task.TaskId);
        builder.Property(task => task.TaskId).HasColumnName("task_id").ValueGeneratedNever();
        builder.Property(task => task.WorkAreaId).HasColumnName("work_area_id").IsRequired();
        builder.Property(task => task.Name).HasColumnName("task_name").HasMaxLength(200).IsRequired();
        builder.Property(task => task.Type).HasColumnName("task_type").HasConversion(EnumText.Converter<ProjectedTaskType>()).HasMaxLength(30).IsRequired();
        builder.Property(task => task.Status).HasColumnName("task_status").HasConversion(EnumText.Converter<ProjectedTaskStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(task => task.PurchaseRequestId).HasColumnName("purchase_request_id");
        builder.Property(task => task.AssigneeUserIds).HasColumnName("assignee_user_ids").HasColumnType("uuid[]").IsRequired();
        builder.Property(task => task.SourceVersion).HasColumnName("source_version").IsRequired();
        builder.Property(task => task.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(task => task.WorkAreaId, "ix_task_projection_work_area");
        builder.HasIndex(task => task.PurchaseRequestId, "ix_task_projection_purchase_request");
    }
}

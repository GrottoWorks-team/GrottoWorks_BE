using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Infrastructure.Data.Configurations;

public sealed class WorkTaskConfiguration : IEntityTypeConfiguration<WorkTask>
{
    public void Configure(EntityTypeBuilder<WorkTask> builder)
    {
        builder.ToTable("task", table =>
        {
            table.HasCheckConstraint(
                "ck_task_purchase_link",
                "(task_type = 'PURCHASE' AND purchase_request_id IS NOT NULL) OR " +
                "(task_type <> 'PURCHASE' AND purchase_request_id IS NULL)");
            table.HasCheckConstraint("ck_task_positive_estimate", "estimated_minutes > 0");
            table.HasCheckConstraint("ck_task_positive_volunteer_count", "required_volunteers > 0");
        });
        builder.HasKey(task => task.Id);
        builder.Property(task => task.Id).HasColumnName("task_id").ValueGeneratedNever();
        builder.Property(task => task.WorkAreaId).HasColumnName("work_area_id").IsRequired();
        builder.Property(task => task.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(task => task.Type).HasColumnName("task_type")
            .HasConversion(EnumText.Converter<TaskType>()).HasMaxLength(30).IsRequired();
        builder.Property(task => task.PurchaseRequestId).HasColumnName("purchase_request_id");
        builder.Property(task => task.Name).HasColumnName("task_name").HasMaxLength(160).IsRequired();
        builder.Property(task => task.Description).HasColumnName("task_description").HasMaxLength(2000);
        builder.Property(task => task.CompletionCriteria).HasColumnName("completion_criteria").HasMaxLength(4000);
        builder.Property(task => task.Priority).HasColumnName("task_priority")
            .HasConversion(EnumText.Converter<TaskPriority>()).HasMaxLength(30).IsRequired();
        builder.Property(task => task.EstimatedMinutes).HasColumnName("estimated_minutes").IsRequired();
        builder.Property(task => task.RequiredVolunteerCount).HasColumnName("required_volunteers").IsRequired();
        builder.Property(task => task.StartAt).HasColumnName("start_at").HasColumnType("timestamp with time zone");
        builder.Property(task => task.Deadline).HasColumnName("deadline")
            .HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(task => task.Status).HasColumnName("task_status")
            .HasConversion(EnumText.Converter<TaskExecution.Domain.Tasks.TaskStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(task => task.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
        builder.Property(task => task.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(500);
        builder.Property(task => task.CancelledBy).HasColumnName("cancelled_by_user_id");
        builder.Property(task => task.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(task => task.CreatedAt).HasColumnName("created_at")
            .HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(task => task.UpdatedAt).HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(task => task.WorkAreaId).HasDatabaseName("ix_task_work_area_id");
        builder.HasIndex(task => new { task.WorkAreaId, task.Status }).HasDatabaseName("ix_task_work_area_status");
        builder.HasIndex(task => task.PurchaseRequestId).HasDatabaseName("ix_task_purchase_request_id");
    }
}

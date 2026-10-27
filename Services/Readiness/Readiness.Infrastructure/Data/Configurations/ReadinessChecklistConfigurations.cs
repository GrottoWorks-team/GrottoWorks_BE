using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Readiness.Domain.Checklists;

namespace Readiness.Infrastructure.Data.Configurations;

/// <summary><c>readiness_checklist</c> (ERD v3.6.2) + one checklist per work area.</summary>
public sealed class ReadinessChecklistConfiguration : IEntityTypeConfiguration<ReadinessChecklist>
{
    public const string WorkAreaIndex = "ux_readiness_checklist_work_area";

    public void Configure(EntityTypeBuilder<ReadinessChecklist> builder)
    {
        builder.ToTable("readiness_checklist");
        builder.HasKey(checklist => checklist.ReadinessChecklistId);
        builder.Property(checklist => checklist.ReadinessChecklistId).HasColumnName("readiness_checklist_id").ValueGeneratedNever();
        // work_area_id and user ids reference other services: no FK (spec 2.1).
        builder.Property(checklist => checklist.WorkAreaId).HasColumnName("work_area_id").IsRequired();
        builder.Property(checklist => checklist.CompletedByUserId).HasColumnName("completed_by_user_id");
        builder.Property(checklist => checklist.Name).HasColumnName("checklist_name").HasMaxLength(ReadinessChecklist.MaxNameLength).IsRequired();
        builder.Property(checklist => checklist.Status).HasColumnName("checklist_status").HasConversion(EnumText.Converter<ChecklistStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(checklist => checklist.ReopenReason).HasColumnName("reopen_reason");
        builder.Property(checklist => checklist.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamp with time zone");
        builder.Property(checklist => checklist.Version).IsRowVersion();

        builder.HasIndex(checklist => checklist.WorkAreaId).IsUnique().HasDatabaseName(WorkAreaIndex);
        builder.HasMany(checklist => checklist.Items).WithOne().HasForeignKey(item => item.ReadinessChecklistId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(checklist => checklist.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>readiness_item</c> (ERD v3.6.2) + technical columns <c>item_position</c>, <c>evidence_file_ids</c>.</summary>
public sealed class ReadinessItemConfiguration : IEntityTypeConfiguration<ReadinessItem>
{
    public void Configure(EntityTypeBuilder<ReadinessItem> builder)
    {
        builder.ToTable("readiness_item");
        builder.HasKey(item => item.ReadinessItemId);
        builder.Property(item => item.ReadinessItemId).HasColumnName("readiness_item_id").ValueGeneratedNever();
        builder.Property(item => item.ReadinessChecklistId).HasColumnName("readiness_checklist_id").IsRequired();
        builder.Property(item => item.CheckedByUserId).HasColumnName("checked_by_user_id");
        builder.Property(item => item.CorrectiveTaskId).HasColumnName("corrective_task_id");
        builder.Property(item => item.Name).HasColumnName("item_name").HasMaxLength(ReadinessItem.MaxNameLength).IsRequired();
        builder.Property(item => item.Description).HasColumnName("item_description");
        builder.Property(item => item.Mandatory).HasColumnName("is_required").HasDefaultValue(true).IsRequired();
        builder.Property(item => item.Status).HasColumnName("item_status").HasConversion(EnumText.Converter<ItemStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(item => item.CheckedAt).HasColumnName("checked_at").HasColumnType("timestamp with time zone");
        builder.Property(item => item.Note).HasColumnName("item_note");
        builder.Property(item => item.Position).HasColumnName("item_position").IsRequired();
        builder.Property(item => item.EvidenceFileIds).HasColumnName("evidence_file_ids").HasColumnType("uuid[]").IsRequired();

        builder.HasIndex(item => item.ReadinessChecklistId, "ix_readiness_item_checklist");
        builder.HasIndex(item => item.CorrectiveTaskId, "ix_readiness_item_corrective_task");
    }
}

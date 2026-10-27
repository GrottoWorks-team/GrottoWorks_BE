using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Domain.Inventory;

namespace Resource.Infrastructure.Data.Configurations;

/// <summary><c>task_material_report</c> (ERD v3.6.2) + unique task + extension column <c>revision_reason</c>.</summary>
public sealed class TaskMaterialReportConfiguration : IEntityTypeConfiguration<TaskMaterialReport>
{
    public const string TaskIndex = "ux_task_material_report_task";

    public void Configure(EntityTypeBuilder<TaskMaterialReport> builder)
    {
        builder.ToTable("task_material_report");
        builder.HasKey(report => report.TaskMaterialReportId);
        builder.Property(report => report.TaskMaterialReportId).HasColumnName("task_material_report_id").ValueGeneratedNever();
        // task_id and user ids reference other services: no FK (spec 2.1).
        builder.Property(report => report.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(report => report.ReportedBy).HasColumnName("reported_by").IsRequired();
        builder.Property(report => report.VerifiedBy).HasColumnName("verified_by");
        builder.Property(report => report.Status).HasColumnName("report_status").HasConversion(EnumText.Converter<MaterialReportStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(report => report.Note).HasColumnName("report_note");
        builder.Property(report => report.RevisionReason).HasColumnName("revision_reason");
        builder.Property(report => report.ReportedAt).HasColumnName("reported_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(report => report.VerifiedAt).HasColumnName("verified_at").HasColumnType("timestamp with time zone");
        builder.Property(report => report.Version).IsRowVersion();

        builder.HasIndex(report => report.TaskId).IsUnique().HasDatabaseName(TaskIndex);
        builder.HasMany(report => report.Items).WithOne().HasForeignKey(item => item.TaskMaterialReportId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(report => report.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>task_material_report_item</c> (ERD v3.6.2): reported and verified figures per allocation item.</summary>
public sealed class TaskMaterialReportItemConfiguration : IEntityTypeConfiguration<TaskMaterialReportItem>
{
    public void Configure(EntityTypeBuilder<TaskMaterialReportItem> builder)
    {
        builder.ToTable("task_material_report_item", table =>
        {
            table.HasCheckConstraint(
                "task_material_report_item_reported_non_negative",
                "\"reported_remaining_quantity\" >= 0 AND \"reported_damaged_quantity\" >= 0 AND \"reported_lost_quantity\" >= 0");
            table.HasCheckConstraint(
                "task_material_report_item_verified_non_negative",
                "(\"verified_remaining_quantity\" IS NULL OR \"verified_remaining_quantity\" >= 0) AND " +
                "(\"verified_damaged_quantity\" IS NULL OR \"verified_damaged_quantity\" >= 0) AND " +
                "(\"verified_lost_quantity\" IS NULL OR \"verified_lost_quantity\" >= 0)");
        });
        builder.HasKey(item => item.TaskMaterialReportItemId);
        builder.Property(item => item.TaskMaterialReportItemId).HasColumnName("task_material_report_item_id").ValueGeneratedNever();
        builder.Property(item => item.TaskMaterialReportId).HasColumnName("task_material_report_id").IsRequired();
        builder.Property(item => item.MaterialAllocationItemId).HasColumnName("material_allocation_item_id").IsRequired();
        builder.Property(item => item.ReportedRemaining).HasColumnName("reported_remaining_quantity").HasPrecision(12, 2).HasDefaultValue(0m).IsRequired();
        builder.Property(item => item.ReportedDamaged).HasColumnName("reported_damaged_quantity").HasPrecision(12, 2).HasDefaultValue(0m).IsRequired();
        builder.Property(item => item.ReportedLost).HasColumnName("reported_lost_quantity").HasPrecision(12, 2).HasDefaultValue(0m).IsRequired();
        builder.Property(item => item.VerifiedRemaining).HasColumnName("verified_remaining_quantity").HasPrecision(12, 2);
        builder.Property(item => item.VerifiedDamaged).HasColumnName("verified_damaged_quantity").HasPrecision(12, 2);
        builder.Property(item => item.VerifiedLost).HasColumnName("verified_lost_quantity").HasPrecision(12, 2);
        builder.Property(item => item.Note).HasColumnName("item_note");
        builder.Ignore(item => item.AllocatedQuantity);

        builder.HasIndex(item => new { item.TaskMaterialReportId, item.MaterialAllocationItemId }).IsUnique()
            .HasDatabaseName("ux_task_material_report_item_report_allocation_item");
        builder.HasOne<MaterialAllocationItem>().WithMany().HasForeignKey(item => item.MaterialAllocationItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;

namespace Resource.Infrastructure.Data.Configurations;

/// <summary><c>material_allocation</c> (ERD v3.6.2, F-RES-15).</summary>
public sealed class MaterialAllocationConfiguration : IEntityTypeConfiguration<MaterialAllocation>
{
    public void Configure(EntityTypeBuilder<MaterialAllocation> builder)
    {
        builder.ToTable("material_allocation");
        builder.HasKey(allocation => allocation.MaterialAllocationId);
        builder.Property(allocation => allocation.MaterialAllocationId).HasColumnName("material_allocation_id").ValueGeneratedNever();
        // work_area_id, task_id and user ids reference other services: no FK (spec 2.1).
        builder.Property(allocation => allocation.WorkAreaId).HasColumnName("work_area_id").IsRequired();
        builder.Property(allocation => allocation.TaskId).HasColumnName("task_id");
        builder.Property(allocation => allocation.AllocatedByUserId).HasColumnName("allocated_by_user_id").IsRequired();
        builder.Property(allocation => allocation.AllocationDate).HasColumnName("allocation_date").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(allocation => allocation.Note).HasColumnName("allocation_note");

        builder.HasIndex(allocation => new { allocation.WorkAreaId, allocation.AllocationDate }, "ix_material_allocation_work_area_date");
        builder.HasIndex(allocation => allocation.TaskId, "ix_material_allocation_task");

        builder.HasMany(allocation => allocation.Items).WithOne().HasForeignKey(item => item.MaterialAllocationId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(allocation => allocation.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>material_allocation_item</c> (ERD v3.6.2): quantity actually handed out — history, never rewritten.</summary>
public sealed class MaterialAllocationItemConfiguration : IEntityTypeConfiguration<MaterialAllocationItem>
{
    public void Configure(EntityTypeBuilder<MaterialAllocationItem> builder)
    {
        builder.ToTable("material_allocation_item", table =>
            table.HasCheckConstraint("material_allocation_item_positive_quantity", "\"allocated_quantity\" > 0"));
        builder.HasKey(item => item.MaterialAllocationItemId);
        builder.Property(item => item.MaterialAllocationItemId).HasColumnName("material_allocation_item_id").ValueGeneratedNever();
        builder.Property(item => item.MaterialAllocationId).HasColumnName("material_allocation_id").IsRequired();
        builder.Property(item => item.MaterialId).HasColumnName("material_id").IsRequired();
        builder.Property(item => item.AllocatedQuantity).HasColumnName("allocated_quantity").HasPrecision(12, 2).IsRequired();

        builder.HasIndex(item => new { item.MaterialAllocationId, item.MaterialId }).IsUnique().HasDatabaseName("ux_material_allocation_item_allocation_material");
        builder.HasIndex(item => item.MaterialId, "ix_material_allocation_item_material");
        builder.HasOne<Material>().WithMany().HasForeignKey(item => item.MaterialId).OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Domain.Catalog;
using Resource.Domain.Purchases;

namespace Resource.Infrastructure.Data.Configurations;

/// <summary><c>self_purchase_receipt</c> (26/11/2026): materials someone bought outside the purchase flow.</summary>
public sealed class SelfPurchaseReceiptConfiguration : IEntityTypeConfiguration<SelfPurchaseReceipt>
{
    public void Configure(EntityTypeBuilder<SelfPurchaseReceipt> builder)
    {
        builder.ToTable("self_purchase_receipt", table =>
            table.HasCheckConstraint("self_purchase_nonnegative_cost", "\"total_cost\" >= 0"));
        builder.HasKey(receipt => receipt.SelfPurchaseReceiptId);
        builder.Property(receipt => receipt.SelfPurchaseReceiptId).HasColumnName("self_purchase_receipt_id").ValueGeneratedNever();
        builder.Property(receipt => receipt.WorkAreaId).HasColumnName("work_area_id").IsRequired();
        builder.Property(receipt => receipt.ParishId).HasColumnName("parish_id").IsRequired();
        builder.Property(receipt => receipt.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(receipt => receipt.TaskId).HasColumnName("task_id");
        builder.Property(receipt => receipt.Source).HasColumnName("source").HasMaxLength(30)
            .HasDefaultValue(SelfPurchaseReceipt.SourceMaterialOfficer).IsRequired();
        builder.Property(receipt => receipt.RecordedByUserId).HasColumnName("recorded_by_user_id").IsRequired();
        builder.Property(receipt => receipt.BuyerName).HasColumnName("buyer_name").HasMaxLength(SelfPurchaseReceipt.MaxBuyerNameLength).IsRequired();
        builder.Property(receipt => receipt.SupplierName).HasColumnName("supplier_name").HasMaxLength(SelfPurchaseReceipt.MaxSupplierLength);
        builder.Property(receipt => receipt.PurchaseDate).HasColumnName("purchase_date").HasColumnType("date").IsRequired();
        builder.Property(receipt => receipt.TotalCost).HasColumnName("total_cost").HasPrecision(14, 2).IsRequired();
        builder.Property(receipt => receipt.InvoiceFileIds).HasColumnName("invoice_file_ids").HasColumnType("uuid[]").IsRequired();
        builder.Property(receipt => receipt.Note).HasColumnName("note").HasMaxLength(SelfPurchaseReceipt.MaxNoteLength);
        builder.Property(receipt => receipt.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(receipt => new { receipt.WorkAreaId, receipt.PurchaseDate }, "ix_self_purchase_work_area_date");
        builder.HasIndex(receipt => new { receipt.SeasonId, receipt.PurchaseDate }, "ix_self_purchase_season_date");
        builder.HasIndex(receipt => receipt.TaskId, "ix_self_purchase_task");
        builder.HasMany(receipt => receipt.Items).WithOne().HasForeignKey(item => item.SelfPurchaseReceiptId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(receipt => receipt.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>self_purchase_item</c> (26/11/2026).</summary>
public sealed class SelfPurchaseItemConfiguration : IEntityTypeConfiguration<SelfPurchaseItem>
{
    public void Configure(EntityTypeBuilder<SelfPurchaseItem> builder)
    {
        builder.ToTable("self_purchase_item", table =>
            table.HasCheckConstraint("self_purchase_item_valid_amounts", "\"quantity\" > 0 AND \"unit_price\" >= 0 AND \"total_price\" >= 0"));
        builder.HasKey(item => item.SelfPurchaseItemId);
        builder.Property(item => item.SelfPurchaseItemId).HasColumnName("self_purchase_item_id").ValueGeneratedNever();
        builder.Property(item => item.SelfPurchaseReceiptId).HasColumnName("self_purchase_receipt_id").IsRequired();
        builder.Property(item => item.MaterialId).HasColumnName("material_id").IsRequired();
        builder.Property(item => item.Quantity).HasColumnName("quantity").HasPrecision(12, 2).IsRequired();
        builder.Property(item => item.UnitPrice).HasColumnName("unit_price").HasPrecision(14, 2).IsRequired();
        builder.Property(item => item.TotalPrice).HasColumnName("total_price").HasPrecision(14, 2).IsRequired();

        builder.HasIndex(item => new { item.SelfPurchaseReceiptId, item.MaterialId }).IsUnique().HasDatabaseName("ux_self_purchase_item_material");
        builder.HasOne<Material>().WithMany().HasForeignKey(item => item.MaterialId).OnDelete(DeleteBehavior.Restrict);
    }
}

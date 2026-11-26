using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;

namespace Resource.Infrastructure.Data.Configurations;

/// <summary><c>parish_purchase_receipt</c> (26/11/2026): materials the parish committee bought for the central warehouse.</summary>
public sealed class ParishPurchaseReceiptConfiguration : IEntityTypeConfiguration<ParishPurchaseReceipt>
{
    public void Configure(EntityTypeBuilder<ParishPurchaseReceipt> builder)
    {
        builder.ToTable("parish_purchase_receipt", table =>
            table.HasCheckConstraint("parish_purchase_nonnegative_cost", "\"total_cost\" >= 0"));
        builder.HasKey(receipt => receipt.ParishPurchaseReceiptId);
        builder.Property(receipt => receipt.ParishPurchaseReceiptId).HasColumnName("parish_purchase_receipt_id").ValueGeneratedNever();
        builder.Property(receipt => receipt.ParishId).HasColumnName("parish_id").IsRequired();
        builder.Property(receipt => receipt.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(receipt => receipt.RecordedByUserId).HasColumnName("recorded_by_user_id").IsRequired();
        builder.Property(receipt => receipt.BuyerName).HasColumnName("buyer_name").HasMaxLength(ParishPurchaseReceipt.MaxBuyerNameLength).IsRequired();
        builder.Property(receipt => receipt.SupplierName).HasColumnName("supplier_name").HasMaxLength(ParishPurchaseReceipt.MaxSupplierLength);
        builder.Property(receipt => receipt.PurchaseDate).HasColumnName("purchase_date").HasColumnType("date").IsRequired();
        builder.Property(receipt => receipt.TotalCost).HasColumnName("total_cost").HasPrecision(14, 2).IsRequired();
        builder.Property(receipt => receipt.InvoiceFileIds).HasColumnName("invoice_file_ids").HasColumnType("uuid[]").IsRequired();
        builder.Property(receipt => receipt.Note).HasColumnName("note").HasMaxLength(ParishPurchaseReceipt.MaxNoteLength);
        builder.Property(receipt => receipt.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(receipt => new { receipt.ParishId, receipt.SeasonId, receipt.PurchaseDate }, "ix_parish_purchase_parish_season_date");
        builder.HasMany(receipt => receipt.Items).WithOne().HasForeignKey(item => item.ParishPurchaseReceiptId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(receipt => receipt.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>parish_purchase_item</c> (26/11/2026).</summary>
public sealed class ParishPurchaseItemConfiguration : IEntityTypeConfiguration<ParishPurchaseItem>
{
    public void Configure(EntityTypeBuilder<ParishPurchaseItem> builder)
    {
        builder.ToTable("parish_purchase_item", table =>
            table.HasCheckConstraint("parish_purchase_item_valid_amounts", "\"quantity\" > 0 AND \"unit_price\" >= 0 AND \"total_price\" >= 0"));
        builder.HasKey(item => item.ParishPurchaseItemId);
        builder.Property(item => item.ParishPurchaseItemId).HasColumnName("parish_purchase_item_id").ValueGeneratedNever();
        builder.Property(item => item.ParishPurchaseReceiptId).HasColumnName("parish_purchase_receipt_id").IsRequired();
        builder.Property(item => item.MaterialId).HasColumnName("material_id").IsRequired();
        builder.Property(item => item.Quantity).HasColumnName("quantity").HasPrecision(12, 2).IsRequired();
        builder.Property(item => item.UnitPrice).HasColumnName("unit_price").HasPrecision(14, 2).IsRequired();
        builder.Property(item => item.TotalPrice).HasColumnName("total_price").HasPrecision(14, 2).IsRequired();

        builder.HasIndex(item => new { item.ParishPurchaseReceiptId, item.MaterialId }).IsUnique()
            .HasDatabaseName("ux_parish_purchase_item_material");
        builder.HasOne<Material>().WithMany().HasForeignKey(item => item.MaterialId).OnDelete(DeleteBehavior.Restrict);
    }
}

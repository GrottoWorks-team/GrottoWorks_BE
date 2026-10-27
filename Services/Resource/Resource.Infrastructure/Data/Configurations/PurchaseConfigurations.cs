using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Domain.Catalog;
using Resource.Domain.Purchases;

namespace Resource.Infrastructure.Data.Configurations;

/// <summary><c>purchase</c> (ERD v3.6.2, F-RES-09/10/11) + technical columns <c>receipt_file_ids</c>, <c>info_request_reason</c>.</summary>
public sealed class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> builder)
    {
        builder.ToTable("purchase", table =>
            table.HasCheckConstraint("purchase_nonnegative_cost", "\"total_cost\" >= 0"));
        builder.HasKey(purchase => purchase.PurchaseId);
        builder.Property(purchase => purchase.PurchaseId).HasColumnName("purchase_id").ValueGeneratedNever();
        builder.Property(purchase => purchase.PurchaseRequestId).HasColumnName("purchase_request_id").IsRequired();
        // task_id and user ids reference other services: no FK (spec 2.1).
        builder.Property(purchase => purchase.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(purchase => purchase.BuyerUserId).HasColumnName("buyer_user_id").IsRequired();
        builder.Property(purchase => purchase.ConfirmedByUserId).HasColumnName("confirmed_by_user_id");
        builder.Property(purchase => purchase.SupplierName).HasColumnName("supplier_name").HasMaxLength(Purchase.MaxSupplierLength);
        builder.Property(purchase => purchase.PurchaseDate).HasColumnName("purchase_date").HasColumnType("date").IsRequired();
        builder.Property(purchase => purchase.TotalCost).HasColumnName("total_cost").HasPrecision(14, 2).IsRequired();
        builder.Property(purchase => purchase.Status).HasColumnName("purchase_status").HasConversion(EnumText.Converter<PurchaseStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(purchase => purchase.ConfirmedAt).HasColumnName("confirmed_at").HasColumnType("timestamp with time zone");
        builder.Property(purchase => purchase.CostOverrunApprovedByUserId).HasColumnName("cost_overrun_approved_by_user_id");
        builder.Property(purchase => purchase.CostOverrunApprovedAt).HasColumnName("cost_overrun_approved_at").HasColumnType("timestamp with time zone");
        builder.Property(purchase => purchase.ReceiptFileIds).HasColumnName("receipt_file_ids").HasColumnType("uuid[]").IsRequired();
        builder.Property(purchase => purchase.InfoRequestReason).HasColumnName("info_request_reason");
        builder.Property(purchase => purchase.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(purchase => purchase.Version).IsRowVersion();

        builder.HasIndex(purchase => purchase.PurchaseRequestId, "ix_purchase_request");
        builder.HasOne<PurchaseRequest>().WithMany().HasForeignKey(purchase => purchase.PurchaseRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(purchase => purchase.Items).WithOne().HasForeignKey(item => item.PurchaseId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(purchase => purchase.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>purchase_item</c> (ERD v3.6.2).</summary>
public sealed class PurchaseItemConfiguration : IEntityTypeConfiguration<PurchaseItem>
{
    public void Configure(EntityTypeBuilder<PurchaseItem> builder)
    {
        builder.ToTable("purchase_item", table =>
        {
            table.HasCheckConstraint("purchase_item_valid_amounts", "\"purchased_quantity\" > 0 AND \"unit_price\" >= 0 AND \"total_price\" >= 0");
            table.HasCheckConstraint("purchase_item_nonnegative_usable", "\"received_usable_quantity\" IS NULL OR \"received_usable_quantity\" >= 0");
        });
        builder.HasKey(item => item.PurchaseItemId);
        builder.Property(item => item.PurchaseItemId).HasColumnName("purchase_item_id").ValueGeneratedNever();
        builder.Property(item => item.PurchaseId).HasColumnName("purchase_id").IsRequired();
        builder.Property(item => item.PurchaseRequestItemId).HasColumnName("purchase_request_item_id").IsRequired();
        builder.Property(item => item.MaterialId).HasColumnName("material_id").IsRequired();
        builder.Property(item => item.PurchasedQuantity).HasColumnName("purchased_quantity").HasPrecision(12, 2).IsRequired();
        builder.Property(item => item.UnitPrice).HasColumnName("unit_price").HasPrecision(14, 2).IsRequired();
        builder.Property(item => item.TotalPrice).HasColumnName("total_price").HasPrecision(14, 2).IsRequired();
        builder.Property(item => item.ReceivedUsableQuantity).HasColumnName("received_usable_quantity").HasPrecision(12, 2);
        builder.Property(item => item.ReceivedUnusableQuantity).HasColumnName("received_unusable_quantity").HasPrecision(12, 2);

        builder.HasIndex(item => new { item.PurchaseId, item.PurchaseRequestItemId }).IsUnique().HasDatabaseName("ux_purchase_item_purchase_request_item");
        builder.HasOne<PurchaseRequestItem>().WithMany().HasForeignKey(item => item.PurchaseRequestItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Material>().WithMany().HasForeignKey(item => item.MaterialId).OnDelete(DeleteBehavior.Restrict);
    }
}

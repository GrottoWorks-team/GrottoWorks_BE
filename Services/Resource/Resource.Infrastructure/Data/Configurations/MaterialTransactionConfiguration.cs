using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;

namespace Resource.Infrastructure.Data.Configurations;

/// <summary><c>material_transaction</c> — append-only stock ledger (ERD v3.6, BR-23/24).</summary>
public sealed class MaterialTransactionConfiguration : IEntityTypeConfiguration<MaterialTransaction>
{
    public const string ReferenceIndex = "ux_material_transaction_reference";
    public const string OpeningStockIndex = "ux_material_transaction_opening_stock";

    public void Configure(EntityTypeBuilder<MaterialTransaction> builder)
    {
        builder.ToTable("material_transaction", table =>
        {
            table.HasCheckConstraint("material_transaction_quantity_non_zero", "\"transaction_quantity\" <> 0");
            // Inflows positive, outflows negative — enforced in the database too.
            table.HasCheckConstraint(
                "material_transaction_quantity_sign",
                "(\"transaction_type\" IN ('OPENING_STOCK','PURCHASE_DELIVERY','DONATION_RECEIPT','TASK_RETURN','TRANSFER_IN') AND \"transaction_quantity\" > 0) OR " +
                "(\"transaction_type\" IN ('TRANSFER_OUT','ADJUSTMENT_DAMAGED','ADJUSTMENT_LOST','ADJUSTMENT_DISPOSED') AND \"transaction_quantity\" < 0)");
        });

        builder.HasKey(transaction => transaction.MaterialTransactionId);
        builder.Property(transaction => transaction.MaterialTransactionId).HasColumnName("material_transaction_id").ValueGeneratedNever();
        builder.Property(transaction => transaction.MaterialId).HasColumnName("material_id").IsRequired();
        // Cross-service reference (ParishCoordination work area): no FK.
        builder.Property(transaction => transaction.WorkAreaId).HasColumnName("work_area_id").IsRequired();
        builder.Property(transaction => transaction.Type).HasColumnName("transaction_type").HasConversion(EnumText.Converter<TransactionType>()).HasMaxLength(40).IsRequired();
        builder.Property(transaction => transaction.Quantity).HasColumnName("transaction_quantity").HasPrecision(12, 2).IsRequired();
        builder.Property(transaction => transaction.TransactionDate).HasColumnName("transaction_date").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(transaction => transaction.ReferenceType).HasColumnName("reference_type").HasConversion(EnumText.NullableConverter<TransactionReferenceType>()).HasMaxLength(40);
        builder.Property(transaction => transaction.ReferenceId).HasColumnName("reference_id");
        builder.Property(transaction => transaction.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(transaction => transaction.Note).HasColumnName("transaction_note");

        // Two indexes on the same columns need explicit EF index names, otherwise the second overrides the first.
        builder.HasIndex(transaction => new { transaction.WorkAreaId, transaction.MaterialId }, "ix_material_transaction_work_area_material");
        builder.HasIndex(transaction => new { transaction.ReferenceType, transaction.ReferenceId, transaction.Type })
            .IsUnique()
            .HasDatabaseName(ReferenceIndex);
        builder.HasIndex(transaction => new { transaction.WorkAreaId, transaction.MaterialId }, OpeningStockIndex)
            .IsUnique()
            .HasFilter("\"transaction_type\" = 'OPENING_STOCK'");

        builder.HasOne<Material>().WithMany().HasForeignKey(transaction => transaction.MaterialId).OnDelete(DeleteBehavior.Restrict);
    }
}

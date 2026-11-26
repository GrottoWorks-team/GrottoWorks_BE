using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;

namespace Resource.Infrastructure.Data.Configurations;

/// <summary><c>parish_material_transaction</c> — append-only ledger of the parish warehouse (per season).</summary>
public sealed class ParishMaterialTransactionConfiguration : IEntityTypeConfiguration<ParishMaterialTransaction>
{
    public const string ReferenceIndex = "ux_parish_material_transaction_reference";
    public const string OpeningStockIndex = "ux_parish_material_transaction_opening_stock";

    public void Configure(EntityTypeBuilder<ParishMaterialTransaction> builder)
    {
        builder.ToTable("parish_material_transaction", table =>
        {
            table.HasCheckConstraint("parish_material_transaction_quantity_non_zero", "\"transaction_quantity\" <> 0");
            table.HasCheckConstraint(
                "parish_material_transaction_quantity_sign",
                "(\"transaction_type\" IN ('OPENING_STOCK','PURCHASE_DELIVERY','DONATION_RECEIPT','DISTRIBUTION_RETURN') AND \"transaction_quantity\" > 0) OR " +
                "(\"transaction_type\" IN ('DISTRIBUTION_OUT','ADJUSTMENT_DAMAGED','ADJUSTMENT_LOST','ADJUSTMENT_DISPOSED') AND \"transaction_quantity\" < 0)");
        });

        builder.HasKey(transaction => transaction.ParishMaterialTransactionId);
        builder.Property(transaction => transaction.ParishMaterialTransactionId)
            .HasColumnName("parish_material_transaction_id").ValueGeneratedNever();
        builder.Property(transaction => transaction.ParishId).HasColumnName("parish_id").IsRequired();
        builder.Property(transaction => transaction.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(transaction => transaction.MaterialId).HasColumnName("material_id").IsRequired();
        builder.Property(transaction => transaction.Type).HasColumnName("transaction_type")
            .HasConversion(EnumText.Converter<TransactionType>()).HasMaxLength(40).IsRequired();
        builder.Property(transaction => transaction.Quantity).HasColumnName("transaction_quantity").HasPrecision(12, 2).IsRequired();
        builder.Property(transaction => transaction.TransactionDate).HasColumnName("transaction_date")
            .HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(transaction => transaction.ReferenceType).HasColumnName("reference_type")
            .HasConversion(EnumText.NullableConverter<TransactionReferenceType>()).HasMaxLength(40);
        builder.Property(transaction => transaction.ReferenceId).HasColumnName("reference_id");
        builder.Property(transaction => transaction.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(transaction => transaction.Note).HasColumnName("transaction_note").HasMaxLength(2000);

        builder.HasIndex(transaction => new { transaction.ParishId, transaction.SeasonId, transaction.MaterialId },
            "ix_parish_material_transaction_parish_season_material");
        builder.HasIndex(transaction => new { transaction.ReferenceType, transaction.ReferenceId, transaction.Type })
            .IsUnique()
            .HasDatabaseName(ReferenceIndex);
        builder.HasIndex(transaction => new { transaction.ParishId, transaction.SeasonId, transaction.MaterialId })
            .IsUnique()
            .HasDatabaseName(OpeningStockIndex)
            .HasFilter("\"transaction_type\" = 'OPENING_STOCK'");

        builder.HasOne<Material>().WithMany().HasForeignKey(transaction => transaction.MaterialId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>parish_distribution</c> — one two-step hand-down from the parish warehouse to a work area.</summary>
public sealed class ParishDistributionConfiguration : IEntityTypeConfiguration<ParishDistribution>
{
    public void Configure(EntityTypeBuilder<ParishDistribution> builder)
    {
        builder.ToTable("parish_distribution");
        builder.HasKey(distribution => distribution.ParishDistributionId);
        builder.Property(distribution => distribution.ParishDistributionId).HasColumnName("parish_distribution_id").ValueGeneratedNever();
        builder.Property(distribution => distribution.ParishId).HasColumnName("parish_id").IsRequired();
        builder.Property(distribution => distribution.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(distribution => distribution.TargetWorkAreaId).HasColumnName("target_work_area_id").IsRequired();
        builder.Property(distribution => distribution.Status).HasColumnName("distribution_status")
            .HasConversion(EnumText.Converter<ParishDistributionStatus>()).HasMaxLength(20).IsRequired();
        builder.Property(distribution => distribution.Origin).HasColumnName("distribution_origin")
            .HasConversion(EnumText.Converter<ParishDistributionOrigin>()).HasMaxLength(20).IsRequired();
        builder.Property(distribution => distribution.IssuedByUserId).HasColumnName("issued_by_user_id").IsRequired();
        builder.Property(distribution => distribution.IssuedAt).HasColumnName("issued_at")
            .HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(distribution => distribution.ReceivedConfirmedByUserId).HasColumnName("received_confirmed_by_user_id");
        builder.Property(distribution => distribution.ReceivedConfirmedAt).HasColumnName("received_confirmed_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(distribution => distribution.Note).HasColumnName("distribution_note").HasMaxLength(ParishDistribution.MaxNoteLength);
        builder.Property(distribution => distribution.CancelReason).HasColumnName("cancel_reason").HasMaxLength(ParishDistribution.MaxReasonLength);
        builder.Property(distribution => distribution.Version).IsRowVersion();

        builder.HasIndex(distribution => new { distribution.ParishId, distribution.SeasonId, distribution.TargetWorkAreaId, distribution.Status },
            "ix_parish_distribution_scope");
        builder.HasIndex(distribution => distribution.TargetWorkAreaId, "ix_parish_distribution_target");
        builder.HasMany(distribution => distribution.Items).WithOne()
            .HasForeignKey(item => item.ParishDistributionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(distribution => distribution.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>parish_distribution_item</c>: issued quantity plus what the receiving officer counted.</summary>
public sealed class ParishDistributionItemConfiguration : IEntityTypeConfiguration<ParishDistributionItem>
{
    public void Configure(EntityTypeBuilder<ParishDistributionItem> builder)
    {
        builder.ToTable("parish_distribution_item", table =>
        {
            table.HasCheckConstraint("parish_distribution_item_issued_positive", "\"issued_quantity\" > 0");
            table.HasCheckConstraint(
                "parish_distribution_item_receipt_valid",
                "(\"received_usable_quantity\" IS NULL OR \"received_usable_quantity\" >= 0) AND " +
                "(\"received_unusable_quantity\" IS NULL OR \"received_unusable_quantity\" >= 0)");
        });
        builder.HasKey(item => item.ParishDistributionItemId);
        builder.Property(item => item.ParishDistributionItemId).HasColumnName("parish_distribution_item_id").ValueGeneratedNever();
        builder.Property(item => item.ParishDistributionId).HasColumnName("parish_distribution_id").IsRequired();
        builder.Property(item => item.MaterialId).HasColumnName("material_id").IsRequired();
        builder.Property(item => item.IssuedQuantity).HasColumnName("issued_quantity").HasPrecision(12, 2).IsRequired();
        builder.Property(item => item.ReceivedUsableQuantity).HasColumnName("received_usable_quantity").HasPrecision(12, 2);
        builder.Property(item => item.ReceivedUnusableQuantity).HasColumnName("received_unusable_quantity").HasPrecision(12, 2);

        builder.HasIndex(item => new { item.ParishDistributionId, item.MaterialId })
            .IsUnique().HasDatabaseName("ux_parish_distribution_item_material");
        builder.HasOne<Material>().WithMany().HasForeignKey(item => item.MaterialId).OnDelete(DeleteBehavior.Restrict);
    }
}

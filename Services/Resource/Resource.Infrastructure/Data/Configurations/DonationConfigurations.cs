using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Domain.Catalog;
using Resource.Domain.Contributions;

namespace Resource.Infrastructure.Data.Configurations;

/// <summary><c>donation</c> (ERD v3.6.2, D-02) with the ERD check constraints.</summary>
public sealed class DonationConfiguration : IEntityTypeConfiguration<Donation>
{
    public void Configure(EntityTypeBuilder<Donation> builder)
    {
        builder.ToTable("donation", table =>
        {
            table.HasCheckConstraint(
                "donation_type_amount_consistency",
                "(\"donation_type\" = 'MONEY' AND (\"pledged_amount\" IS NULL OR \"pledged_amount\" > 0) AND (\"amount\" IS NULL OR \"amount\" >= 0)) OR " +
                "(\"donation_type\" = 'MATERIAL' AND \"amount\" IS NULL AND \"pledged_amount\" IS NULL)");
            table.HasCheckConstraint("donation_material_requires_work_area", "\"donation_type\" <> 'MATERIAL' OR \"work_area_id\" IS NOT NULL");
            table.HasCheckConstraint("donation_cancel_reason", "\"donation_status\" <> 'CANCELLED' OR \"cancel_reason\" IS NOT NULL");
        });

        builder.HasKey(donation => donation.DonationId);
        builder.Property(donation => donation.DonationId).HasColumnName("donation_id").ValueGeneratedNever();
        builder.Property(donation => donation.DonorId).HasColumnName("donor_id").IsRequired();
        // season_id, work_area_id and user ids reference other services: no FK (spec 2.1).
        builder.Property(donation => donation.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(donation => donation.WorkAreaId).HasColumnName("work_area_id");
        builder.Property(donation => donation.Type).HasColumnName("donation_type").HasConversion(EnumText.Converter<DonationType>()).HasMaxLength(30).IsRequired();
        builder.Property(donation => donation.Status).HasColumnName("donation_status").HasConversion(EnumText.Converter<DonationStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(donation => donation.PledgedAmount).HasColumnName("pledged_amount").HasPrecision(14, 2);
        builder.Property(donation => donation.Amount).HasColumnName("amount").HasPrecision(14, 2);
        builder.Property(donation => donation.DonationDate).HasColumnName("donation_date").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(donation => donation.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(donation => donation.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(donation => donation.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.Property(donation => donation.CancelReason).HasColumnName("cancel_reason");
        builder.Property(donation => donation.Note).HasColumnName("donation_note");
        builder.Property(donation => donation.Version).IsRowVersion();

        builder.HasIndex(donation => new { donation.SeasonId, donation.DonationDate }, "ix_donation_season_date");
        builder.HasIndex(donation => donation.DonorId, "ix_donation_donor");

        builder.HasOne<Donor>().WithMany().HasForeignKey(donation => donation.DonorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(donation => donation.Items).WithOne().HasForeignKey(item => item.DonationId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(donation => donation.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(donation => donation.Receipts).WithOne().HasForeignKey(receipt => receipt.DonationId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(donation => donation.Receipts).HasField("_receipts").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>donation_item</c> (ERD v3.6.2): received/unusable are caches of the receipt lines.</summary>
public sealed class DonationItemConfiguration : IEntityTypeConfiguration<DonationItem>
{
    public void Configure(EntityTypeBuilder<DonationItem> builder)
    {
        builder.ToTable("donation_item", table =>
        {
            table.HasCheckConstraint("donation_item_positive_pledge", "\"pledged_quantity\" IS NULL OR \"pledged_quantity\" > 0");
            table.HasCheckConstraint(
                "donation_item_valid_receipt",
                "\"received_quantity\" >= 0 AND \"unusable_quantity\" >= 0 AND \"unusable_quantity\" <= \"received_quantity\"");
        });

        builder.HasKey(item => item.DonationItemId);
        builder.Property(item => item.DonationItemId).HasColumnName("donation_item_id").ValueGeneratedNever();
        builder.Property(item => item.DonationId).HasColumnName("donation_id").IsRequired();
        builder.Property(item => item.MaterialId).HasColumnName("material_id").IsRequired();
        builder.Property(item => item.PledgedQuantity).HasColumnName("pledged_quantity").HasPrecision(12, 2);
        builder.Property(item => item.ReceivedQuantity).HasColumnName("received_quantity").HasPrecision(12, 2).HasDefaultValue(0m).IsRequired();
        builder.Property(item => item.UnusableQuantity).HasColumnName("unusable_quantity").HasPrecision(12, 2).HasDefaultValue(0m).IsRequired();
        builder.Property(item => item.Note).HasColumnName("item_note");

        builder.HasIndex(item => new { item.DonationId, item.MaterialId }).IsUnique().HasDatabaseName("ux_donation_item_donation_material");
        builder.HasOne<Material>().WithMany().HasForeignKey(item => item.MaterialId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>donation_receipt</c> (ERD v3.6.2): one physical receipt occasion.</summary>
public sealed class DonationReceiptConfiguration : IEntityTypeConfiguration<DonationReceipt>
{
    public void Configure(EntityTypeBuilder<DonationReceipt> builder)
    {
        builder.ToTable("donation_receipt", table =>
            table.HasCheckConstraint("donation_receipt_positive_amount", "\"received_amount\" IS NULL OR \"received_amount\" > 0"));

        builder.HasKey(receipt => receipt.DonationReceiptId);
        builder.Property(receipt => receipt.DonationReceiptId).HasColumnName("donation_receipt_id").ValueGeneratedNever();
        builder.Property(receipt => receipt.DonationId).HasColumnName("donation_id").IsRequired();
        builder.Property(receipt => receipt.ReceivedAt).HasColumnName("received_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(receipt => receipt.ReceivedByUserId).HasColumnName("received_by_user_id").IsRequired();
        builder.Property(receipt => receipt.ReceivedAmount).HasColumnName("received_amount").HasPrecision(14, 2);
        builder.Property(receipt => receipt.Note).HasColumnName("receipt_note");
        builder.Property(receipt => receipt.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasMany(receipt => receipt.Items).WithOne().HasForeignKey(line => line.DonationReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(receipt => receipt.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>donation_receipt_item</c> (ERD v3.6.2): usable = received − unusable is what the ledger receives.</summary>
public sealed class DonationReceiptItemConfiguration : IEntityTypeConfiguration<DonationReceiptItem>
{
    public void Configure(EntityTypeBuilder<DonationReceiptItem> builder)
    {
        builder.ToTable("donation_receipt_item", table =>
            table.HasCheckConstraint(
                "donation_receipt_item_valid_quantity",
                "\"received_quantity\" > 0 AND \"unusable_quantity\" >= 0 AND \"unusable_quantity\" <= \"received_quantity\""));

        builder.HasKey(line => line.DonationReceiptItemId);
        builder.Property(line => line.DonationReceiptItemId).HasColumnName("donation_receipt_item_id").ValueGeneratedNever();
        builder.Property(line => line.DonationReceiptId).HasColumnName("donation_receipt_id").IsRequired();
        builder.Property(line => line.DonationItemId).HasColumnName("donation_item_id").IsRequired();
        builder.Property(line => line.ReceivedQuantity).HasColumnName("received_quantity").HasPrecision(12, 2).IsRequired();
        builder.Property(line => line.UnusableQuantity).HasColumnName("unusable_quantity").HasPrecision(12, 2).HasDefaultValue(0m).IsRequired();
        builder.Property(line => line.Note).HasColumnName("item_note");

        builder.HasIndex(line => new { line.DonationReceiptId, line.DonationItemId }).IsUnique().HasDatabaseName("ux_donation_receipt_item_receipt_item");
        builder.HasOne<DonationItem>().WithMany().HasForeignKey(line => line.DonationItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

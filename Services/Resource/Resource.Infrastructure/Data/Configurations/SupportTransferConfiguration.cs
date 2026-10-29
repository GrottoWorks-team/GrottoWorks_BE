using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;

namespace Resource.Infrastructure.Data.Configurations;

/// <summary>Technical table <c>support_transfer</c> (F-RES-20) — Resource side of a TaskExecution support material.</summary>
public sealed class SupportTransferConfiguration : IEntityTypeConfiguration<SupportTransfer>
{
    public void Configure(EntityTypeBuilder<SupportTransfer> builder)
    {
        builder.ToTable("support_transfer", table =>
        {
            table.HasCheckConstraint("support_transfer_positive_quantity", "\"transfer_quantity\" > 0");
            table.HasCheckConstraint("support_transfer_different_work_areas", "\"source_work_area_id\" <> \"target_work_area_id\"");
            table.HasCheckConstraint(
                "support_transfer_valid_receipt",
                "(\"received_usable_quantity\" IS NULL OR \"received_usable_quantity\" >= 0) AND " +
                "(\"received_unusable_quantity\" IS NULL OR \"received_unusable_quantity\" >= 0)");
        });
        builder.HasKey(transfer => transfer.SupportResponseMaterialId);
        builder.Property(transfer => transfer.SupportResponseMaterialId).HasColumnName("support_response_material_id").ValueGeneratedNever();
        builder.Property(transfer => transfer.SourceWorkAreaId).HasColumnName("source_work_area_id").IsRequired();
        builder.Property(transfer => transfer.TargetWorkAreaId).HasColumnName("target_work_area_id").IsRequired();
        builder.Property(transfer => transfer.MaterialId).HasColumnName("material_id").IsRequired();
        builder.Property(transfer => transfer.Quantity).HasColumnName("transfer_quantity").HasPrecision(12, 2).IsRequired();
        builder.Property(transfer => transfer.Status).HasColumnName("transfer_status").HasConversion(EnumText.Converter<SupportTransferStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(transfer => transfer.ApprovedByUserId).HasColumnName("approved_by_user_id").IsRequired();
        builder.Property(transfer => transfer.TransferredAt).HasColumnName("transferred_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(transfer => transfer.ReceivedUsableQuantity).HasColumnName("received_usable_quantity").HasPrecision(12, 2);
        builder.Property(transfer => transfer.ReceivedUnusableQuantity).HasColumnName("received_unusable_quantity").HasPrecision(12, 2);
        builder.Property(transfer => transfer.ReceivedConfirmedByUserId).HasColumnName("received_confirmed_by_user_id");
        builder.Property(transfer => transfer.ReceivedConfirmedAt).HasColumnName("received_confirmed_at").HasColumnType("timestamp with time zone");
        builder.Property(transfer => transfer.Note).HasColumnName("transfer_note");
        builder.Property(transfer => transfer.Version).IsRowVersion();

        builder.HasIndex(transfer => new { transfer.TargetWorkAreaId, transfer.Status }, "ix_support_transfer_target_status");
        builder.HasOne<Material>().WithMany().HasForeignKey(transfer => transfer.MaterialId).OnDelete(DeleteBehavior.Restrict);
    }
}

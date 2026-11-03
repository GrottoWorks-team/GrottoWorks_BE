using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskExecution.Domain.Support;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Support;

public sealed class SupportResponseMaterialConfiguration : IEntityTypeConfiguration<SupportResponseMaterial>
{
    public void Configure(EntityTypeBuilder<SupportResponseMaterial> b)
    {
        b.ToTable("support_response_material",table=>table.HasCheckConstraint("support_material_positive_offer","offered_quantity>0"));b.HasKey(row=>row.Id);
        b.Property(row=>row.Id).HasColumnName("support_response_material_id").ValueGeneratedNever();
        b.Property(row=>row.ResponseId).HasColumnName("support_response_id");b.Property(row=>row.RequestId).HasColumnName("support_request_id");
        b.Property(row=>row.RequestMaterialId).HasColumnName("support_request_material_id");b.Property(row=>row.SourceWorkAreaId).HasColumnName("source_work_area_id");
        b.Property(row=>row.MaterialId).HasColumnName("material_id");b.Property(row=>row.OfferedQuantity).HasColumnName("offered_quantity").HasPrecision(12,2);
        b.Property(row=>row.ApprovedQuantity).HasColumnName("approved_quantity").HasPrecision(12,2);
        b.Property(row=>row.ReceivedUsableQuantity).HasColumnName("received_usable_quantity").HasPrecision(12,2);
        b.Property(row=>row.ReceivedUnusableQuantity).HasColumnName("received_unusable_quantity").HasPrecision(12,2);
        b.Property(row=>row.Status).HasColumnName("material_status").HasMaxLength(30).HasConversion(EnumText.Converter<SupportMaterialStatus>());
        b.Property(row=>row.ApprovedBy).HasColumnName("approved_by_user_id");b.Property(row=>row.ApprovedAt).HasColumnName("approved_at");
        b.Property(row=>row.ReceivedConfirmedBy).HasColumnName("received_confirmed_by_user_id");b.Property(row=>row.ReceivedConfirmedAt).HasColumnName("received_confirmed_at");
        b.Property(row=>row.Note).HasColumnName("material_note").HasMaxLength(2000);
        b.Property(row=>row.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
        b.Ignore(row=>row.ApprovalPending);
        b.Property(row=>row.ApprovalOperationId).HasColumnName("approval_operation_id");
        b.Property(row=>row.ApprovalRequestedQuantity).HasColumnName("approval_requested_quantity").HasPrecision(12,2);
        b.Property(row=>row.ApprovalRequestedBy).HasColumnName("approval_requested_by");
        b.Property(row=>row.ApprovalRequestedAt).HasColumnName("approval_requested_at");
        b.Property(row=>row.ApprovalCorrelationId).HasColumnName("approval_correlation_id").HasMaxLength(200);
        b.Property(row=>row.ApprovalErrorCode).HasColumnName("approval_error_code").HasMaxLength(100);
        b.Property(row=>row.ReviewReason).HasColumnName("review_reason").HasMaxLength(500);
        b.HasIndex(row=>row.Id).HasDatabaseName("ix_support_material_pending_approval")
            .HasFilter("material_status='PENDING' AND approval_operation_id IS NOT NULL AND approval_error_code IS NULL");
        b.HasOne<SupportMaterial>().WithMany().HasForeignKey(row=>new {row.RequestId,row.RequestMaterialId})
            .HasPrincipalKey(row=>new {row.RequestId,row.Id}).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_support_offer_demand");
        b.HasIndex(row=>new {row.RequestId,row.RequestMaterialId}).HasDatabaseName("ix_support_offer_demand");
        b.HasIndex(row=>new {row.RequestId,row.ResponseId}).HasDatabaseName("ix_support_offer_response");
        b.HasIndex(row=>new {row.ResponseId,row.RequestMaterialId,row.SourceWorkAreaId}).IsUnique().HasDatabaseName("ux_support_offer_response_demand_source");
    }
}

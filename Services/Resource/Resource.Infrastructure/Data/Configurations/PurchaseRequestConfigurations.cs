using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Domain.Catalog;
using Resource.Domain.Purchases;

namespace Resource.Infrastructure.Data.Configurations;

/// <summary><c>purchase_request</c> (ERD v3.6, F-RES-06/07).</summary>
public sealed class PurchaseRequestConfiguration : IEntityTypeConfiguration<PurchaseRequest>
{
    public void Configure(EntityTypeBuilder<PurchaseRequest> builder)
    {
        builder.ToTable("purchase_request", table =>
            table.HasCheckConstraint(
                "purchase_request_rejection_note",
                "\"request_status\" <> 'REJECTED' OR \"decision_note\" IS NOT NULL"));

        builder.HasKey(request => request.PurchaseRequestId);
        builder.Property(request => request.PurchaseRequestId).HasColumnName("purchase_request_id").ValueGeneratedNever();
        // work_area_id and user ids reference other services: no FK (spec 2.1).
        builder.Property(request => request.WorkAreaId).HasColumnName("work_area_id").IsRequired();
        builder.Property(request => request.RequestedByUserId).HasColumnName("requested_by_user_id").IsRequired();
        builder.Property(request => request.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(request => request.RequestReason).HasColumnName("request_reason");
        builder.Property(request => request.Status).HasColumnName("request_status").HasConversion(EnumText.Converter<PurchaseRequestStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(request => request.DecisionNote).HasColumnName("decision_note");
        builder.Property(request => request.ApprovedAt).HasColumnName("approved_at").HasColumnType("timestamp with time zone");
        builder.Property(request => request.RejectedAt).HasColumnName("rejected_at").HasColumnType("timestamp with time zone");
        builder.Property(request => request.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(request => request.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.Property(request => request.Version).IsRowVersion();
        builder.Ignore(request => request.EstimatedTotal);

        builder.HasIndex(request => new { request.WorkAreaId, request.Status }, "ix_purchase_request_work_area_status");

        builder.HasMany(request => request.Items).WithOne().HasForeignKey(item => item.PurchaseRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(request => request.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(request => request.StateChanges).WithOne().HasForeignKey(change => change.PurchaseRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(request => request.StateChanges).HasField("_stateChanges").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>purchase_request_item</c> (ERD v3.6) + extension column <c>shortage_override_reason</c>.</summary>
public sealed class PurchaseRequestItemConfiguration : IEntityTypeConfiguration<PurchaseRequestItem>
{
    public const string RequestMaterialIndex = "ux_purchase_request_item_request_material";

    public void Configure(EntityTypeBuilder<PurchaseRequestItem> builder)
    {
        builder.ToTable("purchase_request_item", table =>
        {
            table.HasCheckConstraint("purchase_request_item_positive_quantity", "\"requested_quantity\" > 0");
            table.HasCheckConstraint("purchase_request_item_nonnegative_cost", "\"estimated_cost\" IS NULL OR \"estimated_cost\" >= 0");
        });

        builder.HasKey(item => item.PurchaseRequestItemId);
        builder.Property(item => item.PurchaseRequestItemId).HasColumnName("purchase_request_item_id").ValueGeneratedNever();
        builder.Property(item => item.PurchaseRequestId).HasColumnName("purchase_request_id").IsRequired();
        builder.Property(item => item.MaterialId).HasColumnName("material_id").IsRequired();
        builder.Property(item => item.RequestedQuantity).HasColumnName("requested_quantity").HasPrecision(12, 2).IsRequired();
        builder.Property(item => item.EstimatedCost).HasColumnName("estimated_cost").HasPrecision(14, 2);
        builder.Property(item => item.Note).HasColumnName("item_note");
        builder.Property(item => item.ShortageOverrideReason).HasColumnName("shortage_override_reason");

        builder.HasIndex(item => new { item.PurchaseRequestId, item.MaterialId }).IsUnique().HasDatabaseName(RequestMaterialIndex);

        builder.HasOne<Material>().WithMany().HasForeignKey(item => item.MaterialId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Technical append-only table <c>purchase_request_state_change</c> (getPurchaseRequestHistory).</summary>
public sealed class PurchaseRequestStateChangeConfiguration : IEntityTypeConfiguration<PurchaseRequestStateChange>
{
    public void Configure(EntityTypeBuilder<PurchaseRequestStateChange> builder)
    {
        builder.ToTable("purchase_request_state_change");

        builder.HasKey(change => change.PurchaseRequestStateChangeId);
        builder.Property(change => change.PurchaseRequestStateChangeId).HasColumnName("purchase_request_state_change_id").ValueGeneratedNever();
        builder.Property(change => change.PurchaseRequestId).HasColumnName("purchase_request_id").IsRequired();
        builder.Property(change => change.FromStatus).HasColumnName("from_status").HasConversion(EnumText.NullableConverter<PurchaseRequestStatus>()).HasMaxLength(30);
        builder.Property(change => change.ToStatus).HasColumnName("to_status").HasConversion(EnumText.Converter<PurchaseRequestStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(change => change.ChangedByUserId).HasColumnName("changed_by_user_id").IsRequired();
        builder.Property(change => change.ChangedAt).HasColumnName("changed_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(change => change.Note).HasColumnName("change_note");

        builder.HasIndex(change => new { change.PurchaseRequestId, change.ChangedAt }, "ix_purchase_request_state_change_request");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Reporting.Domain.ReadModels;

namespace Reporting.Infrastructure.Data.Configurations;

// Read models of F-RPT-04/05 (spec §3 "read-model tổng hợp"): technical tables, rebuilt from events, no FK to other services.

public sealed class MaterialLineConfiguration : IEntityTypeConfiguration<MaterialLine>
{
    public void Configure(EntityTypeBuilder<MaterialLine> builder)
    {
        builder.ToTable("rm_material_line");
        builder.HasKey(line => new { line.WorkAreaId, line.MaterialId });
        builder.Property(line => line.WorkAreaId).HasColumnName("work_area_id");
        builder.Property(line => line.MaterialId).HasColumnName("material_id");
        builder.Property(line => line.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(line => line.ParishId).HasColumnName("parish_id").IsRequired();
        builder.Property(line => line.MaterialCode).HasColumnName("material_code").HasMaxLength(50);
        builder.Property(line => line.MaterialName).HasColumnName("material_name").HasMaxLength(150);
        builder.Property(line => line.Unit).HasColumnName("unit").HasMaxLength(30);
        builder.Property(line => line.Required).HasColumnName("required_quantity").HasPrecision(12, 2);
        builder.Property(line => line.ConfirmedAvailable).HasColumnName("confirmed_available").HasPrecision(12, 2);
        builder.Property(line => line.Shortage).HasColumnName("shortage").HasPrecision(12, 2);
        builder.Property(line => line.Pledged).HasColumnName("pledged_quantity").HasPrecision(12, 2);
        builder.Property(line => line.DonatedUsable).HasColumnName("donated_usable").HasPrecision(12, 2);
        builder.Property(line => line.DonatedUnusable).HasColumnName("donated_unusable").HasPrecision(12, 2);
        builder.Property(line => line.PurchasedUsable).HasColumnName("purchased_usable").HasPrecision(12, 2);
        builder.Property(line => line.PurchasedUnusable).HasColumnName("purchased_unusable").HasPrecision(12, 2);
        builder.Property(line => line.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.Property(line => line.Version).IsRowVersion();
        builder.HasIndex(line => new { line.ParishId, line.SeasonId }, "ix_rm_material_line_parish_season");
    }
}

public sealed class PurchaseRequestViewConfiguration : IEntityTypeConfiguration<PurchaseRequestView>
{
    public void Configure(EntityTypeBuilder<PurchaseRequestView> builder)
    {
        builder.ToTable("rm_purchase_request");
        builder.HasKey(request => request.PurchaseRequestId);
        builder.Property(request => request.PurchaseRequestId).HasColumnName("purchase_request_id").ValueGeneratedNever();
        builder.Property(request => request.WorkAreaId).HasColumnName("work_area_id").IsRequired();
        builder.Property(request => request.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(request => request.ParishId).HasColumnName("parish_id").IsRequired();
        builder.Property(request => request.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(request => request.Status).HasColumnName("request_status").HasMaxLength(30).IsRequired();
        builder.Property(request => request.EstimatedTotal).HasColumnName("estimated_total").HasPrecision(14, 2);
        builder.Property(request => request.ActualCost).HasColumnName("actual_cost").HasPrecision(14, 2);
        builder.Property(request => request.Deliveries).HasColumnName("deliveries");
        builder.Property(request => request.SubmittedAt).HasColumnName("submitted_at").HasColumnType("timestamp with time zone");
        builder.Property(request => request.DecidedAt).HasColumnName("decided_at").HasColumnType("timestamp with time zone");
        builder.Property(request => request.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.Property(request => request.Version).IsRowVersion();
        builder.HasIndex(request => request.WorkAreaId, "ix_rm_purchase_request_work_area");
    }
}

public sealed class DonationViewConfiguration : IEntityTypeConfiguration<DonationView>
{
    public void Configure(EntityTypeBuilder<DonationView> builder)
    {
        builder.ToTable("rm_donation");
        builder.HasKey(donation => donation.DonationId);
        builder.Property(donation => donation.DonationId).HasColumnName("donation_id").ValueGeneratedNever();
        builder.Property(donation => donation.DonorId).HasColumnName("donor_id").IsRequired();
        builder.Property(donation => donation.DonorUserId).HasColumnName("donor_user_id");
        builder.Property(donation => donation.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(donation => donation.ParishId).HasColumnName("parish_id").IsRequired();
        builder.Property(donation => donation.WorkAreaId).HasColumnName("work_area_id");
        builder.Property(donation => donation.DonationType).HasColumnName("donation_type").HasMaxLength(30).IsRequired();
        builder.Property(donation => donation.Pledged).HasColumnName("is_pledged");
        builder.Property(donation => donation.PledgedAmount).HasColumnName("pledged_amount").HasPrecision(14, 2);
        builder.Property(donation => donation.PledgedQuantity).HasColumnName("pledged_quantity").HasPrecision(12, 2);
        builder.Property(donation => donation.ReceivedAmount).HasColumnName("received_amount").HasPrecision(14, 2);
        builder.Property(donation => donation.ReceivedUsableQuantity).HasColumnName("received_usable_quantity").HasPrecision(12, 2);
        builder.Property(donation => donation.ReceivedUnusableQuantity).HasColumnName("received_unusable_quantity").HasPrecision(12, 2);
        builder.Property(donation => donation.Receipts).HasColumnName("receipts");
        builder.Property(donation => donation.FirstReceivedAt).HasColumnName("first_received_at").HasColumnType("timestamp with time zone");
        builder.Property(donation => donation.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.Property(donation => donation.Version).IsRowVersion();
        builder.Ignore(donation => donation.Status);
        builder.HasIndex(donation => new { donation.ParishId, donation.SeasonId }, "ix_rm_donation_parish_season");
    }
}

public sealed class AttendanceViewConfiguration : IEntityTypeConfiguration<AttendanceView>
{
    public void Configure(EntityTypeBuilder<AttendanceView> builder)
    {
        builder.ToTable("rm_attendance");
        builder.HasKey(attendance => attendance.AttendanceId);
        builder.Property(attendance => attendance.AttendanceId).HasColumnName("attendance_id").ValueGeneratedNever();
        builder.Property(attendance => attendance.VolunteerUserId).HasColumnName("volunteer_user_id").IsRequired();
        builder.Property(attendance => attendance.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(attendance => attendance.WorkAreaId).HasColumnName("work_area_id").IsRequired();
        builder.Property(attendance => attendance.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(attendance => attendance.ParishId).HasColumnName("parish_id").IsRequired();
        builder.Property(attendance => attendance.ApprovedHours).HasColumnName("approved_hours").HasPrecision(8, 2);
        builder.Property(attendance => attendance.ApprovedAt).HasColumnName("approved_at").HasColumnType("timestamp with time zone");
        builder.Property(attendance => attendance.Version).IsRowVersion();
        builder.HasIndex(attendance => attendance.WorkAreaId, "ix_rm_attendance_work_area");
    }
}

public sealed class ReadinessAreaViewConfiguration : IEntityTypeConfiguration<ReadinessAreaView>
{
    public void Configure(EntityTypeBuilder<ReadinessAreaView> builder)
    {
        builder.ToTable("rm_readiness_area");
        builder.HasKey(area => area.WorkAreaId);
        builder.Property(area => area.WorkAreaId).HasColumnName("work_area_id").ValueGeneratedNever();
        builder.Property(area => area.ReadinessChecklistId).HasColumnName("readiness_checklist_id").IsRequired();
        builder.Property(area => area.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(area => area.ParishId).HasColumnName("parish_id").IsRequired();
        builder.Property(area => area.Status).HasColumnName("checklist_status").HasMaxLength(30).IsRequired();
        builder.Property(area => area.FailedItems).HasColumnName("failed_items");
        builder.Property(area => area.FailedMandatoryItems).HasColumnName("failed_mandatory_items");
        builder.Property(area => area.ReopenCount).HasColumnName("reopen_count");
        builder.Property(area => area.ReadyAt).HasColumnName("ready_at").HasColumnType("timestamp with time zone");
        builder.Property(area => area.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.Property(area => area.Version).IsRowVersion();
    }
}

public sealed class ReadinessHistoryEntryConfiguration : IEntityTypeConfiguration<ReadinessHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ReadinessHistoryEntry> builder)
    {
        builder.ToTable("rm_readiness_history");
        builder.HasKey(entry => entry.ReadinessHistoryEntryId);
        builder.Property(entry => entry.ReadinessHistoryEntryId).HasColumnName("readiness_history_id").ValueGeneratedNever();
        builder.Property(entry => entry.WorkAreaId).HasColumnName("work_area_id").IsRequired();
        builder.Property(entry => entry.ReadinessChecklistId).HasColumnName("readiness_checklist_id").IsRequired();
        builder.Property(entry => entry.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(entry => entry.ParishId).HasColumnName("parish_id").IsRequired();
        builder.Property(entry => entry.Kind).HasColumnName("entry_kind").HasMaxLength(30).IsRequired();
        builder.Property(entry => entry.ItemName).HasColumnName("item_name").HasMaxLength(200);
        builder.Property(entry => entry.Mandatory).HasColumnName("is_required");
        builder.Property(entry => entry.Note).HasColumnName("entry_note");
        builder.Property(entry => entry.ActorUserId).HasColumnName("actor_user_id").IsRequired();
        builder.Property(entry => entry.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entry => entry.WorkAreaId, "ix_rm_readiness_history_work_area");
    }
}

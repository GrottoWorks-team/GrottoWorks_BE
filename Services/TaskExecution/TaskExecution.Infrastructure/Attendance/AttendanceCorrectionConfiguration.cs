using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskExecution.Domain.Attendance;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Attendance;

public sealed class AttendanceCorrectionConfiguration : IEntityTypeConfiguration<AttendanceCorrection>
{
    public void Configure(EntityTypeBuilder<AttendanceCorrection> b)
    {
        b.ToTable("attendance_correction",t=>t.HasCheckConstraint("attendance_correction_valid_range","requested_check_out_at > requested_check_in_at"));b.HasKey(r=>r.Id);
        b.Property(r=>r.Id).HasColumnName("attendance_correction_id").ValueGeneratedNever();
        b.Property(r=>r.AttendanceId).HasColumnName("attendance_id");
        b.Property(r=>r.RequestedBy).HasColumnName("requested_by_user_id");
        b.Property(r=>r.ReviewedBy).HasColumnName("reviewed_by_user_id");
        b.Property(r=>r.OriginalCheckInAt).HasColumnName("original_check_in_at");
        b.Property(r=>r.OriginalCheckOutAt).HasColumnName("original_check_out_at");
        b.Property(r=>r.RequestedCheckInAt).HasColumnName("requested_check_in_at");
        b.Property(r=>r.RequestedCheckOutAt).HasColumnName("requested_check_out_at");
        b.Property(r=>r.SessionVersion).HasColumnName("session_version");
        b.Property(r=>r.Reason).HasColumnName("correction_reason").HasMaxLength(2000);
        b.Property(r=>r.Status).HasColumnName("correction_status").HasMaxLength(30).HasConversion(EnumText.Converter<AttendanceCorrectionStatus>());
        b.Property(r=>r.ReviewNote).HasColumnName("review_note").HasMaxLength(2000);
        b.Property(r=>r.ReviewedAt).HasColumnName("reviewed_at");
        b.Property(r=>r.CreatedAt).HasColumnName("created_at");
        b.Property(r=>r.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
        b.HasOne<AttendanceSession>().WithMany().HasForeignKey(r=>r.AttendanceId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(r=>r.AttendanceId).IsUnique().HasFilter("correction_status = 'PENDING'").HasDatabaseName("ux_attendance_pending_correction");
    }
}

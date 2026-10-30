using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskExecution.Application.Attendance;
using TaskExecution.Domain.Attendance;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Attendance;

public sealed class AttendanceRequestConfiguration : IEntityTypeConfiguration<AttendanceRequest>
{
    public void Configure(EntityTypeBuilder<AttendanceRequest> b)
    {
        b.ToTable("attendance_request"); b.HasKey(r=>r.Id);
        b.Property(r=>r.Id).HasColumnName("attendance_request_id").ValueGeneratedNever();
        b.Property(r=>r.AssignmentId).HasColumnName("task_assignment_id");
        b.Property(r=>r.VolunteerId).HasColumnName("requested_by_user_id");
        b.Property(r=>r.AttendanceId).HasColumnName("attendance_id");
        b.Property(r=>r.Type).HasColumnName("request_type").HasMaxLength(20);
        b.Property(r=>r.RequestedAt).HasColumnName("requested_at");
        b.Property(r=>r.Status).HasColumnName("request_status").HasMaxLength(30).HasConversion(EnumText.Converter<AttendanceRequestStatus>());
        b.Property(r=>r.Note).HasColumnName("request_note").HasMaxLength(2000);
        b.Property(r=>r.ReviewedBy).HasColumnName("reviewed_by_user_id");
        b.Property(r=>r.ReviewNote).HasColumnName("review_note").HasMaxLength(2000);
        b.Property(r=>r.ReviewedAt).HasColumnName("reviewed_at");
        b.Property(r=>r.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
        b.HasOne("TaskExecution.Domain.Assignments.TaskAssignment",null).WithMany().HasForeignKey(nameof(AttendanceRequest.AssignmentId)).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AttendanceSession>().WithMany().HasForeignKey(r=>r.AttendanceId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(r=>r.VolunteerId).IsUnique().HasFilter("request_status = 'PENDING'").HasDatabaseName("ux_attendance_pending_volunteer");
    }
}

public sealed class AttendanceSessionConfiguration : IEntityTypeConfiguration<AttendanceSession>
{
    public void Configure(EntityTypeBuilder<AttendanceSession> b)
    {
        b.ToTable("attendance",t=>t.HasCheckConstraint("attendance_valid_range","check_out_at IS NULL OR check_out_at > check_in_at"));
        b.HasKey(r=>r.Id); b.Property(r=>r.Id).HasColumnName("attendance_id").ValueGeneratedNever();
        b.Property(r=>r.AssignmentId).HasColumnName("task_assignment_id");
        b.Property(r=>r.CheckInRequestId).HasColumnName("check_in_request_id");
        b.Property(r=>r.CheckInAt).HasColumnName("check_in_at");
        b.Property(r=>r.CheckOutAt).HasColumnName("check_out_at");
        b.Property(r=>r.WorkingHours).HasColumnName("working_hours").HasPrecision(6,2);
        b.Property(r=>r.ApprovedMinutes).HasColumnName("approved_minutes");
        b.Property(r=>r.Status).HasColumnName("attendance_status").HasMaxLength(30).HasConversion(EnumText.Converter<AttendanceStatus>());
        b.Property(r=>r.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
        b.HasOne("TaskExecution.Domain.Assignments.TaskAssignment",null).WithMany().HasForeignKey(nameof(AttendanceSession.AssignmentId)).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AttendanceRequest>().WithMany().HasForeignKey(r=>r.CheckInRequestId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(r=>r.CheckInRequestId).IsUnique();
        b.HasIndex(r=>r.AssignmentId).IsUnique().HasFilter("check_out_at IS NULL").HasDatabaseName("ux_attendance_open_assignment");
    }
}

public sealed class AttendanceReplayConfiguration : IEntityTypeConfiguration<AttendanceReplay>
{
    public void Configure(EntityTypeBuilder<AttendanceReplay> b)
    {
        b.ToTable("attendance_idempotency"); b.HasKey(r=>new {r.ActorId,r.Key});
        b.Property(r=>r.ActorId).HasColumnName("actor_id"); b.Property(r=>r.Key).HasColumnName("idempotency_key");
        b.Property(r=>r.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64);
        b.Property(r=>r.Result).HasColumnName("result").HasColumnType("jsonb");
        b.Property(r=>r.CreatedAt).HasColumnName("created_at");
    }
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;
[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261030070000_AddAttendanceCorrections")]
public sealed class AddAttendanceCorrections : Migration
{
    protected override void Up(MigrationBuilder b)=>b.Sql("""
        CREATE TABLE attendance_correction (
          attendance_correction_id uuid PRIMARY KEY,
          attendance_id uuid NOT NULL REFERENCES attendance(attendance_id) ON DELETE RESTRICT,
          requested_by_user_id uuid NOT NULL, reviewed_by_user_id uuid,
          original_check_in_at timestamptz NOT NULL, original_check_out_at timestamptz,
          requested_check_in_at timestamptz NOT NULL, requested_check_out_at timestamptz NOT NULL,
          session_version integer NOT NULL, correction_reason varchar(2000) NOT NULL,
          correction_status varchar(30) NOT NULL, review_note varchar(2000), reviewed_at timestamptz,
          created_at timestamptz NOT NULL, version integer NOT NULL DEFAULT 1,
          CONSTRAINT attendance_correction_valid_range CHECK(requested_check_out_at>requested_check_in_at));
        CREATE UNIQUE INDEX ux_attendance_pending_correction ON attendance_correction(attendance_id) WHERE correction_status='PENDING';
        """);
    protected override void Down(MigrationBuilder b)=>b.Sql("DROP TABLE attendance_correction;");
}

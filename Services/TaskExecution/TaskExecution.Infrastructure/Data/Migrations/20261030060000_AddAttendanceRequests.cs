using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261030060000_AddAttendanceRequests")]
public sealed class AddAttendanceRequests : Migration
{
    protected override void Up(MigrationBuilder b) => b.Sql("""
        CREATE TABLE attendance_request (
          attendance_request_id uuid PRIMARY KEY,
          task_assignment_id uuid NOT NULL REFERENCES task_assignment(task_assignment_id) ON DELETE RESTRICT,
          requested_by_user_id uuid NOT NULL, attendance_id uuid,
          request_type varchar(20) NOT NULL, requested_at timestamptz NOT NULL,
          request_status varchar(30) NOT NULL, request_note varchar(2000),
          reviewed_by_user_id uuid, review_note varchar(2000), reviewed_at timestamptz,
          version integer NOT NULL DEFAULT 1);
        CREATE INDEX "IX_attendance_request_task_assignment_id" ON attendance_request(task_assignment_id);
        CREATE UNIQUE INDEX ux_attendance_pending_volunteer ON attendance_request(requested_by_user_id) WHERE request_status='PENDING';
        CREATE TABLE attendance (
          attendance_id uuid PRIMARY KEY,
          task_assignment_id uuid NOT NULL REFERENCES task_assignment(task_assignment_id) ON DELETE RESTRICT,
          check_in_request_id uuid NOT NULL REFERENCES attendance_request(attendance_request_id) ON DELETE RESTRICT,
          check_in_at timestamptz NOT NULL, check_out_at timestamptz,
          working_hours numeric(6,2), approved_minutes integer NOT NULL,
          attendance_status varchar(30) NOT NULL, version integer NOT NULL DEFAULT 1,
          CONSTRAINT attendance_valid_range CHECK(check_out_at IS NULL OR check_out_at > check_in_at));
        CREATE UNIQUE INDEX "IX_attendance_check_in_request_id" ON attendance(check_in_request_id);
        CREATE UNIQUE INDEX ux_attendance_open_assignment ON attendance(task_assignment_id) WHERE check_out_at IS NULL;
        ALTER TABLE attendance_request ADD CONSTRAINT fk_attendance_request_session FOREIGN KEY(attendance_id) REFERENCES attendance(attendance_id) ON DELETE RESTRICT;
        CREATE INDEX "IX_attendance_request_attendance_id" ON attendance_request(attendance_id);
        CREATE TABLE attendance_idempotency (
          actor_id uuid NOT NULL, idempotency_key uuid NOT NULL, fingerprint varchar(64) NOT NULL,
          result jsonb NOT NULL, created_at timestamptz NOT NULL, PRIMARY KEY(actor_id,idempotency_key));
        """);
    protected override void Down(MigrationBuilder b) => b.Sql("DROP TABLE attendance_idempotency; ALTER TABLE attendance_request DROP CONSTRAINT fk_attendance_request_session; DROP TABLE attendance; DROP TABLE attendance_request;");
}

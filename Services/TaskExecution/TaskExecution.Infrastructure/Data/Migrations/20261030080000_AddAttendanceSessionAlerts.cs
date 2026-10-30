using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;
[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261030080000_AddAttendanceSessionAlerts")]
public sealed class AddAttendanceSessionAlerts : Migration
{
    protected override void Up(MigrationBuilder b)=>b.Sql("""
        CREATE TABLE attendance_session_alert (
          attendance_id uuid NOT NULL REFERENCES attendance(attendance_id) ON DELETE RESTRICT,
          threshold_minutes integer NOT NULL, created_at timestamptz NOT NULL,
          PRIMARY KEY(attendance_id,threshold_minutes));
        """);
    protected override void Down(MigrationBuilder b)=>b.Sql("DROP TABLE attendance_session_alert;");
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261030030000_AddTaskAssignments")]
public sealed class AddTaskAssignments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE task_assignment (
            task_assignment_id uuid PRIMARY KEY, task_id uuid NOT NULL REFERENCES task(task_id) ON DELETE RESTRICT,
            volunteer_user_id uuid NOT NULL, assignment_type varchar(40) NOT NULL, assignment_status varchar(30) NOT NULL,
            requested_at timestamptz, assigned_at timestamptz, approved_at timestamptz, rejected_at timestamptz,
            assignment_note varchar(2000), assigned_by_user_id uuid, override_capacity boolean NOT NULL,
            override_skill_mismatch boolean NOT NULL, override_reason varchar(500), version integer NOT NULL DEFAULT 1);
        CREATE UNIQUE INDEX ux_task_assignment_active_volunteer ON task_assignment(task_id,volunteer_user_id)
            WHERE assignment_status IN ('PENDING','ACCEPTED','IN_PROGRESS','COMPLETED');
        CREATE INDEX ix_task_assignment_volunteer ON task_assignment(volunteer_user_id);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("task_assignment");
}

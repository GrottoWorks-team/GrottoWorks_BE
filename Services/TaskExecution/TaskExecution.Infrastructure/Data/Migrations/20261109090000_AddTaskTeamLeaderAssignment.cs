using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261109090000_AddTaskTeamLeaderAssignment")]
public sealed class AddTaskTeamLeaderAssignment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "team_leader_assignment_id", table: "task", type: "uuid", nullable: true);
        migrationBuilder.AddUniqueConstraint(
            name: "ak_task_assignment_task_id_assignment_id", table: "task_assignment",
            columns: ["task_id", "task_assignment_id"]);
        migrationBuilder.CreateIndex(
            name: "ix_task_team_leader_assignment", table: "task",
            columns: ["task_id", "team_leader_assignment_id"]);
        migrationBuilder.AddForeignKey(
            name: "fk_task_team_leader_assignment", table: "task",
            columns: ["task_id", "team_leader_assignment_id"],
            principalTable: "task_assignment", principalColumns: ["task_id", "task_assignment_id"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "fk_task_team_leader_assignment", table: "task");
        migrationBuilder.DropIndex(name: "ix_task_team_leader_assignment", table: "task");
        migrationBuilder.DropUniqueConstraint(name: "ak_task_assignment_task_id_assignment_id", table: "task_assignment");
        migrationBuilder.DropColumn(name: "team_leader_assignment_id", table: "task");
    }
}

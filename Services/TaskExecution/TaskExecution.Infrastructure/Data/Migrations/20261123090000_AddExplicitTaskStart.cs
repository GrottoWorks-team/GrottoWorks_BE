using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261123090000_AddExplicitTaskStart")]
public sealed class AddExplicitTaskStart : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "started_at", table: "task", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "started_by_user_id", table: "task", type: "uuid", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "started_at", table: "task");
        migrationBuilder.DropColumn(name: "started_by_user_id", table: "task");
    }
}

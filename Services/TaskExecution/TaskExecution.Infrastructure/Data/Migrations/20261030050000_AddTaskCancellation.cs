using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261030050000_AddTaskCancellation")]
public sealed class AddTaskCancellation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE task ADD COLUMN cancellation_reason varchar(500),
            ADD COLUMN cancelled_by_user_id uuid, ADD COLUMN cancelled_at timestamptz;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE task DROP COLUMN cancellation_reason, DROP COLUMN cancelled_by_user_id, DROP COLUMN cancelled_at;
        """);
}

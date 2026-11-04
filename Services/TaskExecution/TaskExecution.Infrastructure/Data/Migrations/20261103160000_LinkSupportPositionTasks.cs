using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261103160000_LinkSupportPositionTasks")]
public sealed class LinkSupportPositionTasks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE support_request_position ADD COLUMN task_id uuid NULL;
        ALTER TABLE support_request_position ADD CONSTRAINT fk_support_position_task
          FOREIGN KEY (task_id) REFERENCES task(task_id) ON DELETE RESTRICT;
        CREATE INDEX ix_support_request_position_task_id ON support_request_position(task_id);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP INDEX ix_support_request_position_task_id;
        ALTER TABLE support_request_position DROP CONSTRAINT fk_support_position_task;
        ALTER TABLE support_request_position DROP COLUMN task_id;
        """);
}

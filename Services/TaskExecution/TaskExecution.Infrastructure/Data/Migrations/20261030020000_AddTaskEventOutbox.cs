using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261030020000_AddTaskEventOutbox")]
public sealed class AddTaskEventOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE task_event_outbox (
            event_id uuid PRIMARY KEY, event_type varchar(100) NOT NULL,
            envelope jsonb NOT NULL, created_at timestamptz NOT NULL, published_at timestamptz NULL);
        CREATE INDEX ix_task_event_outbox_pending ON task_event_outbox(created_at) WHERE published_at IS NULL;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("task_event_outbox");
}

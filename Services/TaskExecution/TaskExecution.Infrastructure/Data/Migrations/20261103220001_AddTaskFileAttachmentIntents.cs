using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace TaskExecution.Infrastructure.Data.Migrations;
[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261103220001_AddTaskFileAttachmentIntents")]
public sealed class AddTaskFileAttachmentIntents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        CREATE TABLE task_file_attachment_intent (intent_id uuid PRIMARY KEY,task_id uuid NOT NULL,task_progress_id uuid NOT NULL,
          file_id uuid NOT NULL,uploader_id uuid NOT NULL,created_at timestamptz NOT NULL,next_attempt_at timestamptz NOT NULL,
          attempts integer NOT NULL,completed_at timestamptz,outcome varchar(30));
        CREATE INDEX ix_task_file_intents_pending ON task_file_attachment_intent(next_attempt_at) WHERE completed_at IS NULL;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        DROP TABLE task_file_attachment_intent;
        """);
}

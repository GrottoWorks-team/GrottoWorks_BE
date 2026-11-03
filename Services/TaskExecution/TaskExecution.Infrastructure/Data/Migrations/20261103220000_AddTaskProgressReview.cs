using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace TaskExecution.Infrastructure.Data.Migrations;
[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261103220000_AddTaskProgressReview")]
public sealed class AddTaskProgressReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        CREATE TABLE task_progress (
          task_progress_id uuid PRIMARY KEY,task_id uuid NOT NULL REFERENCES task(task_id),submitted_by_user_id uuid NOT NULL,
          progress_percent integer NOT NULL,progress_note varchar(2000),submitted_at timestamptz NOT NULL,is_submission boolean NOT NULL,
          approved_by_user_id uuid,approved_at timestamptz,
          CONSTRAINT ck_task_progress_percent CHECK(progress_percent BETWEEN 0 AND 100),
          CONSTRAINT ck_task_progress_approval CHECK((approved_by_user_id IS NULL AND approved_at IS NULL) OR
            (approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL AND progress_percent=100 AND approved_at>=submitted_at)));
        CREATE INDEX ix_task_progress_history ON task_progress(task_id,submitted_at);
        CREATE TABLE progress_evidence (progress_evidence_id uuid PRIMARY KEY,task_progress_id uuid NOT NULL REFERENCES task_progress(task_progress_id),
          file_id uuid NOT NULL UNIQUE,evidence_caption varchar(255),uploaded_at timestamptz NOT NULL);
        CREATE TABLE task_completion_review (review_id uuid PRIMARY KEY,task_id uuid NOT NULL,task_progress_id uuid NOT NULL UNIQUE REFERENCES task_progress(task_progress_id),
          reviewed_by_user_id uuid NOT NULL,approved boolean NOT NULL,reason varchar(2000),reviewed_at timestamptz NOT NULL);
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        DROP TABLE task_completion_review;DROP TABLE progress_evidence;DROP TABLE task_progress;
        """);
}

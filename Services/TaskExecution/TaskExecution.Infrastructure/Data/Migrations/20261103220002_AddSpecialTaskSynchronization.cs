using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace TaskExecution.Infrastructure.Data.Migrations;
[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261103220002_AddSpecialTaskSynchronization")]
public sealed class AddSpecialTaskSynchronization : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        CREATE TABLE corrective_task_replay (idempotency_key varchar(200) PRIMARY KEY,readiness_item_id uuid NOT NULL UNIQUE,
          task_id uuid NOT NULL,fingerprint varchar(64) NOT NULL,response jsonb NOT NULL);
        CREATE TABLE purchase_approval_projection (purchase_request_id uuid PRIMARY KEY,work_area_id uuid NOT NULL,decision varchar(30) NOT NULL,decided_at timestamptz NOT NULL);
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        DROP TABLE purchase_approval_projection;DROP TABLE corrective_task_replay;
        """);
}

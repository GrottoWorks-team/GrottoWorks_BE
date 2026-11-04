using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261104011000_AlignEvidenceIndexes")]
public sealed class AlignEvidenceIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateIndex(
        name: "IX_progress_evidence_task_progress_id", table: "progress_evidence", column: "task_progress_id");

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropIndex(
        name: "IX_progress_evidence_task_progress_id", table: "progress_evidence");
}

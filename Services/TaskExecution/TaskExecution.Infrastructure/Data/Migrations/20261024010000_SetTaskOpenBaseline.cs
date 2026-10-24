using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261024010000_SetTaskOpenBaseline")]
public partial class SetTaskOpenBaseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE task
            SET task_status = 'OPEN'
            WHERE task_status = 'DRAFT';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // OPEN already existed in the previous model. Reverting these rows to DRAFT
        // would also rewrite tasks that were legitimately OPEN before this migration.
    }
}

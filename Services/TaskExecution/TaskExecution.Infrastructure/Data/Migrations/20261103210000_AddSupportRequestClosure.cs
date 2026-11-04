using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261103210000_AddSupportRequestClosure")]
public sealed class AddSupportRequestClosure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        ALTER TABLE support_request ADD COLUMN closed_by_user_id uuid,
          ADD COLUMN closed_at timestamp with time zone, ADD COLUMN close_reason varchar(500);
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        ALTER TABLE support_request DROP COLUMN closed_by_user_id,DROP COLUMN closed_at,DROP COLUMN close_reason;
        """);
}

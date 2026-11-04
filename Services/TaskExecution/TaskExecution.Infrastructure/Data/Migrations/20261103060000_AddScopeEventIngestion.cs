using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261103060000_AddScopeEventIngestion")]
public sealed class AddScopeEventIngestion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE scope_event_inbox (consumer varchar(100) NOT NULL, event_id uuid NOT NULL,
          processed_at timestamptz NOT NULL, PRIMARY KEY(consumer, event_id));
        CREATE TABLE user_status_projection (user_id uuid PRIMARY KEY, parish_id uuid,
          status varchar(30) NOT NULL, source_version bigint NOT NULL);
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("scope_event_inbox");
        migrationBuilder.DropTable("user_status_projection");
    }
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Resource.Infrastructure.Data.Migrations;

[DbContext(typeof(ResourceDbContext))]
[Migration("20261103070000_AddScopeReplicationInbox")]
public sealed class AddScopeReplicationInbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE scope_replication_inbox (consumer varchar(100) NOT NULL,
          event_id uuid NOT NULL, processed_at timestamptz NOT NULL, PRIMARY KEY (consumer, event_id));
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("scope_replication_inbox");
}

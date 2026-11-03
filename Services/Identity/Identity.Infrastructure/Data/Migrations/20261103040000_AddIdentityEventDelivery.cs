using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Identity.Infrastructure.Data.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("20261103040000_AddIdentityEventDelivery")]
public sealed class AddIdentityEventDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE app_user ADD COLUMN status_version bigint NOT NULL DEFAULT 1;
        CREATE TABLE identity_event_outbox (
          event_id uuid PRIMARY KEY, aggregate_id uuid NOT NULL,
          sequence bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
          event_type varchar(100) NOT NULL, envelope jsonb NOT NULL,
          created_at timestamptz NOT NULL, published_at timestamptz,
          attempts integer NOT NULL DEFAULT 0, next_attempt_at timestamptz NOT NULL,
          last_error varchar(500));
        CREATE INDEX ix_identity_event_outbox_pending ON identity_event_outbox(created_at) WHERE published_at IS NULL;
        CREATE INDEX ix_identity_event_outbox_aggregate_sequence ON identity_event_outbox(aggregate_id, sequence);
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("identity_event_outbox");
        migrationBuilder.DropColumn("status_version", "app_user");
    }
}

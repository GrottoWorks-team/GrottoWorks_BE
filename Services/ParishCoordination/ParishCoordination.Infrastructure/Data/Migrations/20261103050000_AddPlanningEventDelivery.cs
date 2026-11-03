using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ParishCoordination.Infrastructure.Data.Migrations;

[DbContext(typeof(ParishCoordinationDbContext))]
[Migration("20261103050000_AddPlanningEventDelivery")]
public sealed class AddPlanningEventDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE planning_event_outbox (
          event_id uuid PRIMARY KEY, aggregate_id uuid NOT NULL, source_version bigint NOT NULL,
          sequence bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
          event_type varchar(100) NOT NULL, envelope jsonb NOT NULL,
          created_at timestamptz NOT NULL, published_at timestamptz,
          attempts integer NOT NULL DEFAULT 0, next_attempt_at timestamptz NOT NULL,
          last_error varchar(500));
        CREATE INDEX ix_planning_event_outbox_pending ON planning_event_outbox(created_at) WHERE published_at IS NULL;
        CREATE INDEX ix_planning_event_outbox_aggregate_sequence ON planning_event_outbox(aggregate_id, sequence);
        CREATE UNIQUE INDEX ux_planning_event_source ON planning_event_outbox(aggregate_id, source_version, event_type);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("planning_event_outbox");
}

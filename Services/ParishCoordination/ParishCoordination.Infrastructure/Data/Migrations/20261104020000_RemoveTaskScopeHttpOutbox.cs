using Microsoft.EntityFrameworkCore.Migrations;

namespace ParishCoordination.Infrastructure.Data.Migrations;

public partial class RemoveTaskScopeHttpOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Serialize with planning writes and dispatcher/reconciliation while checking this database's delivery proof.
        migrationBuilder.Sql("""
            SELECT pg_advisory_xact_lock(71110305);
            SELECT pg_advisory_xact_lock(71110304);
            DO $guard$
            BEGIN
              IF EXISTS (
                SELECT 1 FROM christmas_season s
                WHERE NOT EXISTS (
                  SELECT 1 FROM planning_event_outbox p
                  WHERE p.event_type = 'scope.snapshot' AND p.published_at IS NOT NULL
                    AND p.aggregate_id = s.season_id AND p.source_version = s.version
                    AND p.envelope->>'scopeType' = 'season'))
                OR EXISTS (
                  SELECT 1 FROM work_area a
                  WHERE NOT EXISTS (
                    SELECT 1 FROM planning_event_outbox p
                    WHERE p.event_type = 'scope.snapshot' AND p.published_at IS NOT NULL
                      AND p.aggregate_id = a.work_area_id AND p.source_version = a.version
                      AND p.envelope->>'scopeType' = 'work-area'))
              THEN
                RAISE EXCEPTION 'HTTP scope cleanup requires published planning scope snapshots for every current Season and Work Area version; reconcile and drain the broker outbox before retrying.';
              END IF;
            END $guard$;
            """);
        migrationBuilder.DropTable("task_scope_outbox");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Restore only the historical schema; retired HTTP delivery records are not reconstructed.
        migrationBuilder.Sql("""
            CREATE TABLE task_scope_outbox (
              id uuid NOT NULL CONSTRAINT "PK_task_scope_outbox" PRIMARY KEY,
              scope_type varchar(20) NOT NULL, aggregate_id uuid NOT NULL, source_version bigint NOT NULL,
              payload jsonb NOT NULL, created_at timestamptz NOT NULL, attempts integer NOT NULL,
              next_attempt_at timestamptz NOT NULL, delivered_at timestamptz, last_error varchar(500));
            CREATE UNIQUE INDEX ux_task_scope_outbox_version
              ON task_scope_outbox(scope_type, aggregate_id, source_version);
            CREATE INDEX ix_task_scope_outbox_pending
              ON task_scope_outbox(next_attempt_at) WHERE delivered_at IS NULL;
            """);
    }
}

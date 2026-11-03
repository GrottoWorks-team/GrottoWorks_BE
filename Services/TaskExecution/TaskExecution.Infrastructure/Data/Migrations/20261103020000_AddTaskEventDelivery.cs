using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261103020000_AddTaskEventDelivery")]
public sealed class AddTaskEventDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE task_event_outbox ADD COLUMN aggregate_id uuid;
        UPDATE task_event_outbox SET aggregate_id = (envelope->>'aggregateId')::uuid;
        ALTER TABLE task_event_outbox ALTER COLUMN aggregate_id SET NOT NULL;
        ALTER TABLE task_event_outbox ADD COLUMN sequence bigint;
        WITH ordered AS (
          SELECT event_id, row_number() OVER (ORDER BY created_at, event_id) AS position FROM task_event_outbox
        ) UPDATE task_event_outbox target SET sequence = ordered.position FROM ordered WHERE target.event_id = ordered.event_id;
        ALTER TABLE task_event_outbox ALTER COLUMN sequence SET NOT NULL;
        ALTER TABLE task_event_outbox ALTER COLUMN sequence ADD GENERATED ALWAYS AS IDENTITY;
        SELECT setval(pg_get_serial_sequence('task_event_outbox', 'sequence'),
          COALESCE((SELECT MAX(sequence) FROM task_event_outbox), 1),
          EXISTS(SELECT 1 FROM task_event_outbox));
        ALTER TABLE task_event_outbox ADD COLUMN attempts integer NOT NULL DEFAULT 0;
        ALTER TABLE task_event_outbox ADD COLUMN next_attempt_at timestamptz;
        UPDATE task_event_outbox SET next_attempt_at = created_at;
        ALTER TABLE task_event_outbox ALTER COLUMN next_attempt_at SET NOT NULL;
        ALTER TABLE task_event_outbox ADD COLUMN last_error varchar(500);
        CREATE INDEX ix_task_event_outbox_aggregate_sequence ON task_event_outbox(aggregate_id, sequence);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP INDEX ix_task_event_outbox_aggregate_sequence;
        ALTER TABLE task_event_outbox DROP COLUMN aggregate_id, DROP COLUMN sequence,
          DROP COLUMN attempts, DROP COLUMN next_attempt_at, DROP COLUMN last_error;
        """);
}

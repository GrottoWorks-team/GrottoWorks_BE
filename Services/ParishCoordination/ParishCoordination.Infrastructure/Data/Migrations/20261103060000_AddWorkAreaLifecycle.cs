using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ParishCoordination.Infrastructure.Data.Migrations;

[DbContext(typeof(ParishCoordinationDbContext))]
[Migration("20261103060000_AddWorkAreaLifecycle")]
public sealed class AddWorkAreaLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE lifecycle_task_projection (
          task_id uuid PRIMARY KEY, work_area_id uuid NOT NULL, season_id uuid NOT NULL,
          parish_id uuid NOT NULL, source_version bigint NOT NULL,
          status varchar(30) NOT NULL, task_type varchar(20));
        CREATE INDEX "IX_lifecycle_task_projection_work_area_id_status"
          ON lifecycle_task_projection(work_area_id, status);
        CREATE TABLE lifecycle_readiness_projection (
          work_area_id uuid PRIMARY KEY, checklist_id uuid NOT NULL, decision_at timestamptz NOT NULL,
          reopened boolean NOT NULL, invalidated boolean NOT NULL, applied boolean NOT NULL);
        CREATE TABLE lifecycle_inbox (
          event_id uuid PRIMARY KEY, work_area_id uuid NOT NULL, season_id uuid NOT NULL,
          parish_id uuid NOT NULL, kind varchar(30) NOT NULL, envelope jsonb NOT NULL,
          received_at timestamptz NOT NULL, processed_at timestamptz);
        CREATE INDEX "IX_lifecycle_inbox_work_area_id_received_at"
          ON lifecycle_inbox(work_area_id, received_at) WHERE processed_at IS NULL;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("lifecycle_inbox");
        migrationBuilder.DropTable("lifecycle_readiness_projection");
        migrationBuilder.DropTable("lifecycle_task_projection");
    }
}

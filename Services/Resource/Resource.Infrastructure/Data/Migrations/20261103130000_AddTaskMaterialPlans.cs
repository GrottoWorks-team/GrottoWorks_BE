using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Resource.Infrastructure.Data.Migrations;

[DbContext(typeof(ResourceDbContext))]
[Migration("20261103130000_AddTaskMaterialPlans")]
public sealed class AddTaskMaterialPlans : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE task_material_plan (task_id uuid PRIMARY KEY, work_area_id uuid NOT NULL,
          parish_id uuid NOT NULL, season_id uuid NOT NULL, source_version bigint NOT NULL, updated_at timestamptz NOT NULL);
        CREATE INDEX ix_task_material_plan_work_area_id ON task_material_plan(work_area_id);
        CREATE TABLE task_material_plan_item (task_id uuid NOT NULL REFERENCES task_material_plan(task_id) ON DELETE CASCADE,
          material_id uuid NOT NULL, required_quantity numeric(12,2) NOT NULL, material_note varchar(2000),
          PRIMARY KEY(task_id, material_id), CONSTRAINT task_material_plan_quantity_positive CHECK(required_quantity > 0));
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("task_material_plan_item");
        migrationBuilder.DropTable("task_material_plan");
    }
}

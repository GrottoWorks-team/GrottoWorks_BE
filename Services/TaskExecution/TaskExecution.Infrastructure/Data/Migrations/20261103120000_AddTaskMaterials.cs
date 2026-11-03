using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261103120000_AddTaskMaterials")]
public sealed class AddTaskMaterials : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE task_material (task_material_id uuid PRIMARY KEY,
          task_id uuid NOT NULL REFERENCES task(task_id) ON DELETE CASCADE,
          material_id uuid NOT NULL, required_quantity numeric(12,2) NOT NULL,
          material_note varchar(2000), CONSTRAINT task_material_positive_quantity CHECK(required_quantity > 0));
        CREATE UNIQUE INDEX ux_task_material_task_id_material_id ON task_material(task_id, material_id);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("task_material");
}

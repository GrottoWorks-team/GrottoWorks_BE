using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskExecution.Infrastructure.Data.Migrations;

public partial class AddTask : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "task",
            columns: table => new
            {
                task_id = table.Column<Guid>(type: "uuid", nullable: false),
                work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                task_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                purchase_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                task_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                task_description = table.Column<string>(type: "text", nullable: true),
                completion_criteria = table.Column<string>(type: "text", nullable: true),
                task_priority = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                estimated_hours = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                required_volunteer_count = table.Column<int>(type: "integer", nullable: false),
                start_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                task_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_task", x => x.task_id);
                table.CheckConstraint(
                    "ck_task_purchase_link",
                    "(task_type = 'PURCHASE' AND purchase_request_id IS NOT NULL) OR " +
                    "(task_type <> 'PURCHASE' AND purchase_request_id IS NULL)");
            });

        migrationBuilder.CreateIndex("ix_task_purchase_request_id", "task", "purchase_request_id");
        migrationBuilder.CreateIndex("ix_task_work_area_id", "task", "work_area_id");
        migrationBuilder.CreateIndex("ix_task_work_area_status", "task", new[] { "work_area_id", "task_status" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "task");
}

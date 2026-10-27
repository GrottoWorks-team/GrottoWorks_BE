using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FRES17_TaskMaterialReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_material_report",
                columns: table => new
                {
                    task_material_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_by = table.Column<Guid>(type: "uuid", nullable: false),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    report_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    report_note = table.Column<string>(type: "text", nullable: true),
                    revision_reason = table.Column<string>(type: "text", nullable: true),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_material_report", x => x.task_material_report_id);
                });

            migrationBuilder.CreateTable(
                name: "task_material_report_item",
                columns: table => new
                {
                    task_material_report_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_material_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_allocation_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_remaining_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    reported_damaged_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    reported_lost_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    verified_remaining_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    verified_damaged_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    verified_lost_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    item_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_material_report_item", x => x.task_material_report_item_id);
                    table.CheckConstraint("task_material_report_item_reported_non_negative", "\"reported_remaining_quantity\" >= 0 AND \"reported_damaged_quantity\" >= 0 AND \"reported_lost_quantity\" >= 0");
                    table.CheckConstraint("task_material_report_item_verified_non_negative", "(\"verified_remaining_quantity\" IS NULL OR \"verified_remaining_quantity\" >= 0) AND (\"verified_damaged_quantity\" IS NULL OR \"verified_damaged_quantity\" >= 0) AND (\"verified_lost_quantity\" IS NULL OR \"verified_lost_quantity\" >= 0)");
                    table.ForeignKey(
                        name: "FK_task_material_report_item_material_allocation_item_material~",
                        column: x => x.material_allocation_item_id,
                        principalTable: "material_allocation_item",
                        principalColumn: "material_allocation_item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_task_material_report_item_task_material_report_task_materia~",
                        column: x => x.task_material_report_id,
                        principalTable: "task_material_report",
                        principalColumn: "task_material_report_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_task_material_report_task",
                table: "task_material_report",
                column: "task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_material_report_item_material_allocation_item_id",
                table: "task_material_report_item",
                column: "material_allocation_item_id");

            migrationBuilder.CreateIndex(
                name: "ux_task_material_report_item_report_allocation_item",
                table: "task_material_report_item",
                columns: new[] { "task_material_report_id", "material_allocation_item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_material_report_item");

            migrationBuilder.DropTable(
                name: "task_material_report");
        }
    }
}

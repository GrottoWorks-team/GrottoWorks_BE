using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FRES15_MaterialAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "material_allocation",
                columns: table => new
                {
                    material_allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allocated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allocation_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    allocation_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_allocation", x => x.material_allocation_id);
                });

            migrationBuilder.CreateTable(
                name: "material_allocation_item",
                columns: table => new
                {
                    material_allocation_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allocated_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_allocation_item", x => x.material_allocation_item_id);
                    table.CheckConstraint("material_allocation_item_positive_quantity", "\"allocated_quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_material_allocation_item_material_allocation_material_alloc~",
                        column: x => x.material_allocation_id,
                        principalTable: "material_allocation",
                        principalColumn: "material_allocation_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_material_allocation_item_material_material_id",
                        column: x => x.material_id,
                        principalTable: "material",
                        principalColumn: "material_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_material_allocation_task",
                table: "material_allocation",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_allocation_work_area_date",
                table: "material_allocation",
                columns: new[] { "work_area_id", "allocation_date" });

            migrationBuilder.CreateIndex(
                name: "ix_material_allocation_item_material",
                table: "material_allocation_item",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ux_material_allocation_item_allocation_material",
                table: "material_allocation_item",
                columns: new[] { "material_allocation_id", "material_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "material_allocation_item");

            migrationBuilder.DropTable(
                name: "material_allocation");
        }
    }
}

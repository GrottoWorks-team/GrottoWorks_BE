using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FRES03_MaterialRequirement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "material_requirement",
                columns: table => new
                {
                    material_requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    required_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    existing_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    requirement_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    requirement_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_requirement", x => x.material_requirement_id);
                    table.CheckConstraint("material_requirement_existing_non_negative", "\"existing_quantity\" >= 0");
                    table.CheckConstraint("material_requirement_required_positive", "\"required_quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_material_requirement_material_material_id",
                        column: x => x.material_id,
                        principalTable: "material",
                        principalColumn: "material_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_material_requirement_material_id",
                table: "material_requirement",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ux_material_requirement_work_area_material",
                table: "material_requirement",
                columns: new[] { "work_area_id", "material_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "material_requirement");
        }
    }
}

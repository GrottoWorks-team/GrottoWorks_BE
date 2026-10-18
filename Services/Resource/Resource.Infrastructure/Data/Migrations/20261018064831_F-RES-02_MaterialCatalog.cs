using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FRES02_MaterialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "material_category",
                columns: table => new
                {
                    material_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    category_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    category_description = table.Column<string>(type: "text", nullable: true),
                    category_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_category", x => x.material_category_id);
                });

            migrationBuilder.CreateTable(
                name: "material",
                columns: table => new
                {
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    material_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    material_unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    material_description = table.Column<string>(type: "text", nullable: true),
                    material_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material", x => x.material_id);
                    table.ForeignKey(
                        name: "FK_material_material_category_material_category_id",
                        column: x => x.material_category_id,
                        principalTable: "material_category",
                        principalColumn: "material_category_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_material_material_category_id",
                table: "material",
                column: "material_category_id");

            migrationBuilder.CreateIndex(
                name: "ux_material_material_code",
                table: "material",
                column: "material_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_material_category_category_code",
                table: "material_category",
                column: "category_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "material");

            migrationBuilder.DropTable(
                name: "material_category");
        }
    }
}

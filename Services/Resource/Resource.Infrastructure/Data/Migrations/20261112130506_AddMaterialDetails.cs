using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "material_minimum_stock",
                table: "material",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "material_reference_unit_price",
                table: "material",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "material_specification",
                table: "material",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "material_supplier_name",
                table: "material",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "material_minimum_stock",
                table: "material");

            migrationBuilder.DropColumn(
                name: "material_reference_unit_price",
                table: "material");

            migrationBuilder.DropColumn(
                name: "material_specification",
                table: "material");

            migrationBuilder.DropColumn(
                name: "material_supplier_name",
                table: "material");
        }
    }
}

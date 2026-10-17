using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParishCoordination.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkAreaCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_area_category",
                columns: table => new
                {
                    work_area_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    category_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    category_description = table.Column<string>(type: "text", nullable: true),
                    category_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_area_category", x => x.work_area_category_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_area_category_category_code",
                table: "work_area_category",
                column: "category_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_area_category");
        }
    }
}

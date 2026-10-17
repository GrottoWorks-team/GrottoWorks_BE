using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParishCoordination.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkAreaVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "work_area",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                table: "work_area");
        }
    }
}

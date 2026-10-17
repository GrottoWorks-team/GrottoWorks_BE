using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParishCoordination.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "christmas_season",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                table: "christmas_season");
        }
    }
}

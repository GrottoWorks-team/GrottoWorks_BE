using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParishCoordination.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddParishVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE parish SET parish_status = 'INACTIVE' " +
                "WHERE parish_status = 'ARCHIVED';");

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "parish",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                table: "parish");
        }
    }
}

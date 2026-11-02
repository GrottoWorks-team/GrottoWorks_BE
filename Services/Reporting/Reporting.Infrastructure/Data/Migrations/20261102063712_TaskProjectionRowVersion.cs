using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reporting.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TaskProjectionRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "task_projection",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "task_projection");
        }
    }
}

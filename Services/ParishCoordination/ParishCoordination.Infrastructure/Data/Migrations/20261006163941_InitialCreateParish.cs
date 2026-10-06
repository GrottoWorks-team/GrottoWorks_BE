using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParishCoordination.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreateParish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parish",
                columns: table => new
                {
                    parish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parish_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    parish_address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    parish_description = table.Column<string>(type: "text", nullable: true),
                    parish_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parish", x => x.parish_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parish");
        }
    }
}

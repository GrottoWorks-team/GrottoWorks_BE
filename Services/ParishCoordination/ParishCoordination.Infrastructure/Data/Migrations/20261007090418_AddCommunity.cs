using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParishCoordination.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "community",
                columns: table => new
                {
                    community_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    community_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    community_description = table.Column<string>(type: "text", nullable: true),
                    community_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community", x => x.community_id);
                    table.ForeignKey(
                        name: "FK_community_parish_parish_id",
                        column: x => x.parish_id,
                        principalTable: "parish",
                        principalColumn: "parish_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_community_parish_id",
                table: "community",
                column: "parish_id");

            migrationBuilder.CreateIndex(
                name: "IX_community_parish_id_community_name",
                table: "community",
                columns: new[] { "parish_id", "community_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "community");
        }
    }
}

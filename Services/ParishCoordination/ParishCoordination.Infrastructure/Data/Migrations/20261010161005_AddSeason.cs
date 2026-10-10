using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParishCoordination.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "christmas_season",
                columns: table => new
                {
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    season_year = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    season_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    season_description = table.Column<string>(type: "text", nullable: true),
                    estimated_budget = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_christmas_season", x => x.season_id);
                    table.ForeignKey(
                        name: "FK_christmas_season_parish_parish_id",
                        column: x => x.parish_id,
                        principalTable: "parish",
                        principalColumn: "parish_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_christmas_season_parish_id_season_name",
                table: "christmas_season",
                columns: new[] { "parish_id", "season_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_christmas_season_parish_id_season_year",
                table: "christmas_season",
                columns: new[] { "parish_id", "season_year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "christmas_season");
        }
    }
}

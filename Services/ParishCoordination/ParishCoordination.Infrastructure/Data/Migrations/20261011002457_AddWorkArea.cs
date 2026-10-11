using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParishCoordination.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_area",
                columns: table => new
                {
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    community_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_area_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leader_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    material_officer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_area_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    work_area_description = table.Column<string>(type: "text", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    work_area_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_area", x => x.work_area_id);
                    table.ForeignKey(
                        name: "FK_work_area_christmas_season_season_id",
                        column: x => x.season_id,
                        principalTable: "christmas_season",
                        principalColumn: "season_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_area_community_community_id",
                        column: x => x.community_id,
                        principalTable: "community",
                        principalColumn: "community_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_area_community_id",
                table: "work_area",
                column: "community_id");

            migrationBuilder.CreateIndex(
                name: "IX_work_area_season_id",
                table: "work_area",
                column: "season_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_area");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FRES01_WorkAreaScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_area_scope",
                columns: table => new
                {
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_area_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    work_area_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    season_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    community_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leader_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    material_officer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_area_scope", x => x.work_area_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_area_scope_parish_id",
                table: "work_area_scope",
                column: "parish_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_area_scope_season_id",
                table: "work_area_scope",
                column: "season_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_area_scope");
        }
    }
}

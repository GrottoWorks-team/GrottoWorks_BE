using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Readiness.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "readiness_checklist",
                columns: table => new
                {
                    readiness_checklist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checklist_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    checklist_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reopen_reason = table.Column<string>(type: "text", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_readiness_checklist", x => x.readiness_checklist_id);
                });

            migrationBuilder.CreateTable(
                name: "task_projection",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    task_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    task_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    purchase_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignee_user_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    source_version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_projection", x => x.task_id);
                });

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

            migrationBuilder.CreateTable(
                name: "readiness_item",
                columns: table => new
                {
                    readiness_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    readiness_checklist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    corrective_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    item_description = table.Column<string>(type: "text", nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    item_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    item_note = table.Column<string>(type: "text", nullable: true),
                    item_position = table.Column<int>(type: "integer", nullable: false),
                    evidence_file_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_readiness_item", x => x.readiness_item_id);
                    table.ForeignKey(
                        name: "FK_readiness_item_readiness_checklist_readiness_checklist_id",
                        column: x => x.readiness_checklist_id,
                        principalTable: "readiness_checklist",
                        principalColumn: "readiness_checklist_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_readiness_checklist_work_area",
                table: "readiness_checklist",
                column: "work_area_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_readiness_item_checklist",
                table: "readiness_item",
                column: "readiness_checklist_id");

            migrationBuilder.CreateIndex(
                name: "ix_readiness_item_corrective_task",
                table: "readiness_item",
                column: "corrective_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_projection_purchase_request",
                table: "task_projection",
                column: "purchase_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_projection_work_area",
                table: "task_projection",
                column: "work_area_id");

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
                name: "readiness_item");

            migrationBuilder.DropTable(
                name: "task_projection");

            migrationBuilder.DropTable(
                name: "work_area_scope");

            migrationBuilder.DropTable(
                name: "readiness_checklist");
        }
    }
}

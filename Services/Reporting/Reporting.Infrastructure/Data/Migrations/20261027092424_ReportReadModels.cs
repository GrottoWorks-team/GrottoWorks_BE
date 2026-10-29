using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reporting.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportReadModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "due_at",
                table: "task_projection",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "rm_attendance",
                columns: table => new
                {
                    attendance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    volunteer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rm_attendance", x => x.attendance_id);
                });

            migrationBuilder.CreateTable(
                name: "rm_donation",
                columns: table => new
                {
                    donation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    donor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    donor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: true),
                    donation_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_pledged = table.Column<bool>(type: "boolean", nullable: false),
                    pledged_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    pledged_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    received_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    received_usable_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    received_unusable_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    receipts = table.Column<int>(type: "integer", nullable: false),
                    first_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rm_donation", x => x.donation_id);
                });

            migrationBuilder.CreateTable(
                name: "rm_material_line",
                columns: table => new
                {
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    material_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    required_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    confirmed_available = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    shortage = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    pledged_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    donated_usable = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    donated_unusable = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    purchased_usable = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    purchased_unusable = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rm_material_line", x => new { x.work_area_id, x.material_id });
                });

            migrationBuilder.CreateTable(
                name: "rm_purchase_request",
                columns: table => new
                {
                    purchase_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    estimated_total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    actual_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    deliveries = table.Column<int>(type: "integer", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rm_purchase_request", x => x.purchase_request_id);
                });

            migrationBuilder.CreateTable(
                name: "rm_readiness_area",
                columns: table => new
                {
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    readiness_checklist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checklist_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    failed_items = table.Column<int>(type: "integer", nullable: false),
                    failed_mandatory_items = table.Column<int>(type: "integer", nullable: false),
                    reopen_count = table.Column<int>(type: "integer", nullable: false),
                    ready_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rm_readiness_area", x => x.work_area_id);
                });

            migrationBuilder.CreateTable(
                name: "rm_readiness_history",
                columns: table => new
                {
                    readiness_history_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    readiness_checklist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    item_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: true),
                    entry_note = table.Column<string>(type: "text", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rm_readiness_history", x => x.readiness_history_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rm_attendance_work_area",
                table: "rm_attendance",
                column: "work_area_id");

            migrationBuilder.CreateIndex(
                name: "ix_rm_donation_parish_season",
                table: "rm_donation",
                columns: new[] { "parish_id", "season_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rm_material_line_parish_season",
                table: "rm_material_line",
                columns: new[] { "parish_id", "season_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rm_purchase_request_work_area",
                table: "rm_purchase_request",
                column: "work_area_id");

            migrationBuilder.CreateIndex(
                name: "ix_rm_readiness_history_work_area",
                table: "rm_readiness_history",
                column: "work_area_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rm_attendance");

            migrationBuilder.DropTable(
                name: "rm_donation");

            migrationBuilder.DropTable(
                name: "rm_material_line");

            migrationBuilder.DropTable(
                name: "rm_purchase_request");

            migrationBuilder.DropTable(
                name: "rm_readiness_area");

            migrationBuilder.DropTable(
                name: "rm_readiness_history");

            migrationBuilder.DropColumn(
                name: "due_at",
                table: "task_projection");
        }
    }
}

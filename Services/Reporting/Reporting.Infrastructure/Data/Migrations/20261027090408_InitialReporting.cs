using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Reporting.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contribution_type",
                columns: table => new
                {
                    contribution_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contribution_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    contribution_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contribution_description = table.Column<string>(type: "text", nullable: true),
                    contribution_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contribution_type", x => x.contribution_type_id);
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
                name: "recognition_rule",
                columns: table => new
                {
                    recognition_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contribution_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    rule_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    rule_points = table.Column<int>(type: "integer", nullable: false),
                    rule_description = table.Column<string>(type: "text", nullable: true),
                    rule_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recognition_rule", x => x.recognition_rule_id);
                    table.CheckConstraint("recognition_rule_valid_period", "\"effective_to\" IS NULL OR \"effective_to\" >= \"effective_from\"");
                    table.ForeignKey(
                        name: "FK_recognition_rule_contribution_type_contribution_type_id",
                        column: x => x.contribution_type_id,
                        principalTable: "contribution_type",
                        principalColumn: "contribution_type_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "contribution_type",
                columns: new[] { "contribution_type_id", "contribution_code", "contribution_description", "contribution_name", "contribution_status" },
                values: new object[,]
                {
                    { new Guid("0f3c2a10-0000-4000-8000-000000000001"), "TASK_COMPLETION", "Task được Leader duyệt hoàn thành.", "Hoàn thành task", "ACTIVE" },
                    { new Guid("0f3c2a10-0000-4000-8000-000000000002"), "VOLUNTEER_HOURS", "Giờ làm đã được Leader duyệt (BR-21).", "Giờ làm được duyệt", "ACTIVE" },
                    { new Guid("0f3c2a10-0000-4000-8000-000000000003"), "PURCHASE_ASSISTANCE", "Purchase đã được xác nhận giao.", "Hỗ trợ mua sắm", "ACTIVE" },
                    { new Guid("0f3c2a10-0000-4000-8000-000000000004"), "MONETARY_SUPPORT", "Donation MONEY đã thực nhận.", "Đóng góp tiền", "ACTIVE" },
                    { new Guid("0f3c2a10-0000-4000-8000-000000000005"), "MATERIAL_DONATION", "Donation MATERIAL đã thực nhận.", "Đóng góp vật tư", "ACTIVE" },
                    { new Guid("0f3c2a10-0000-4000-8000-000000000006"), "SUPPORT_ASSISTANCE", "Thành viên hỗ trợ đã hoàn thành công việc.", "Hỗ trợ community khác", "ACTIVE" }
                });

            migrationBuilder.CreateIndex(
                name: "ux_contribution_type_code",
                table: "contribution_type",
                column: "contribution_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recognition_rule_contribution_type",
                table: "recognition_rule",
                column: "contribution_type_id");

            migrationBuilder.CreateIndex(
                name: "ux_recognition_rule_name_version",
                table: "recognition_rule",
                columns: new[] { "rule_name", "rule_version" },
                unique: true);

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
                name: "recognition_rule");

            migrationBuilder.DropTable(
                name: "task_projection");

            migrationBuilder.DropTable(
                name: "work_area_scope");

            migrationBuilder.DropTable(
                name: "contribution_type");
        }
    }
}

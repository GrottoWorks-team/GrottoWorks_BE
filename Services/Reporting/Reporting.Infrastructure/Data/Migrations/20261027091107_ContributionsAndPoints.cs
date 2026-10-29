using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reporting.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContributionsAndPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contribution",
                columns: table => new
                {
                    contribution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contributed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    donation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contribution_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contribution_description = table.Column<string>(type: "text", nullable: true),
                    contribution_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reference_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    donor_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contribution", x => x.contribution_id);
                    table.CheckConstraint("contribution_single_source", "(\"donation_id\" IS NOT NULL AND \"contributed_by_user_id\" IS NULL AND \"reference_type\" IS NULL AND \"reference_id\" IS NULL) OR (\"donation_id\" IS NULL AND \"contributed_by_user_id\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_contribution_contribution_type_contribution_type_id",
                        column: x => x.contribution_type_id,
                        principalTable: "contribution_type",
                        principalColumn: "contribution_type_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "processed_event",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_event", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "recognition_point",
                columns: table => new
                {
                    recognition_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    awarded_to_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recognition_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contribution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    awarded_points = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recognition_point", x => x.recognition_point_id);
                    table.ForeignKey(
                        name: "FK_recognition_point_contribution_contribution_id",
                        column: x => x.contribution_id,
                        principalTable: "contribution",
                        principalColumn: "contribution_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recognition_point_recognition_rule_recognition_rule_id",
                        column: x => x.recognition_rule_id,
                        principalTable: "recognition_rule",
                        principalColumn: "recognition_rule_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contribution_contributed_by",
                table: "contribution",
                column: "contributed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_contribution_contribution_type_id",
                table: "contribution",
                column: "contribution_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_contribution_donor_user",
                table: "contribution",
                column: "donor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_contribution_parish_season",
                table: "contribution",
                columns: new[] { "parish_id", "season_id" });

            migrationBuilder.CreateIndex(
                name: "ux_contribution_donation",
                table: "contribution",
                column: "donation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_contribution_reference_user",
                table: "contribution",
                columns: new[] { "reference_type", "reference_id", "contributed_by_user_id" },
                unique: true,
                filter: "\"donation_id\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_recognition_point_recognition_rule_id",
                table: "recognition_point",
                column: "recognition_rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_recognition_point_user_season",
                table: "recognition_point",
                columns: new[] { "awarded_to_user_id", "season_id" });

            migrationBuilder.CreateIndex(
                name: "ux_recognition_point_contribution_rule_user",
                table: "recognition_point",
                columns: new[] { "contribution_id", "recognition_rule_id", "awarded_to_user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_event");

            migrationBuilder.DropTable(
                name: "recognition_point");

            migrationBuilder.DropTable(
                name: "contribution");
        }
    }
}

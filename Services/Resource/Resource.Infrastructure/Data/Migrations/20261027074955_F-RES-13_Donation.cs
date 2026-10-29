using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FRES13_Donation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "donation",
                columns: table => new
                {
                    donation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    donor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: true),
                    donation_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    donation_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    pledged_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    donation_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "text", nullable: true),
                    donation_note = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_donation", x => x.donation_id);
                    table.CheckConstraint("donation_cancel_reason", "\"donation_status\" <> 'CANCELLED' OR \"cancel_reason\" IS NOT NULL");
                    table.CheckConstraint("donation_material_requires_work_area", "\"donation_type\" <> 'MATERIAL' OR \"work_area_id\" IS NOT NULL");
                    table.CheckConstraint("donation_type_amount_consistency", "(\"donation_type\" = 'MONEY' AND (\"pledged_amount\" IS NULL OR \"pledged_amount\" > 0) AND (\"amount\" IS NULL OR \"amount\" >= 0)) OR (\"donation_type\" = 'MATERIAL' AND \"amount\" IS NULL AND \"pledged_amount\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_donation_donor_donor_id",
                        column: x => x.donor_id,
                        principalTable: "donor",
                        principalColumn: "donor_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "donation_item",
                columns: table => new
                {
                    donation_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    donation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pledged_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    received_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    unusable_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    item_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_donation_item", x => x.donation_item_id);
                    table.CheckConstraint("donation_item_positive_pledge", "\"pledged_quantity\" IS NULL OR \"pledged_quantity\" > 0");
                    table.CheckConstraint("donation_item_valid_receipt", "\"received_quantity\" >= 0 AND \"unusable_quantity\" >= 0 AND \"unusable_quantity\" <= \"received_quantity\"");
                    table.ForeignKey(
                        name: "FK_donation_item_donation_donation_id",
                        column: x => x.donation_id,
                        principalTable: "donation",
                        principalColumn: "donation_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_donation_item_material_material_id",
                        column: x => x.material_id,
                        principalTable: "material",
                        principalColumn: "material_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "donation_receipt",
                columns: table => new
                {
                    donation_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    donation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    receipt_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_donation_receipt", x => x.donation_receipt_id);
                    table.CheckConstraint("donation_receipt_positive_amount", "\"received_amount\" IS NULL OR \"received_amount\" > 0");
                    table.ForeignKey(
                        name: "FK_donation_receipt_donation_donation_id",
                        column: x => x.donation_id,
                        principalTable: "donation",
                        principalColumn: "donation_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "donation_receipt_item",
                columns: table => new
                {
                    donation_receipt_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    donation_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    donation_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    unusable_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    item_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_donation_receipt_item", x => x.donation_receipt_item_id);
                    table.CheckConstraint("donation_receipt_item_valid_quantity", "\"received_quantity\" > 0 AND \"unusable_quantity\" >= 0 AND \"unusable_quantity\" <= \"received_quantity\"");
                    table.ForeignKey(
                        name: "FK_donation_receipt_item_donation_item_donation_item_id",
                        column: x => x.donation_item_id,
                        principalTable: "donation_item",
                        principalColumn: "donation_item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_donation_receipt_item_donation_receipt_donation_receipt_id",
                        column: x => x.donation_receipt_id,
                        principalTable: "donation_receipt",
                        principalColumn: "donation_receipt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_donation_donor",
                table: "donation",
                column: "donor_id");

            migrationBuilder.CreateIndex(
                name: "ix_donation_season_date",
                table: "donation",
                columns: new[] { "season_id", "donation_date" });

            migrationBuilder.CreateIndex(
                name: "IX_donation_item_material_id",
                table: "donation_item",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ux_donation_item_donation_material",
                table: "donation_item",
                columns: new[] { "donation_id", "material_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_donation_receipt_donation_id",
                table: "donation_receipt",
                column: "donation_id");

            migrationBuilder.CreateIndex(
                name: "IX_donation_receipt_item_donation_item_id",
                table: "donation_receipt_item",
                column: "donation_item_id");

            migrationBuilder.CreateIndex(
                name: "ux_donation_receipt_item_receipt_item",
                table: "donation_receipt_item",
                columns: new[] { "donation_receipt_id", "donation_item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "donation_receipt_item");

            migrationBuilder.DropTable(
                name: "donation_item");

            migrationBuilder.DropTable(
                name: "donation_receipt");

            migrationBuilder.DropTable(
                name: "donation");
        }
    }
}

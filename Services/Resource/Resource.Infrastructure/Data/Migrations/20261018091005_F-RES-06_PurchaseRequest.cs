using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FRES06_PurchaseRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_request",
                columns: table => new
                {
                    purchase_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_reason = table.Column<string>(type: "text", nullable: true),
                    request_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_request", x => x.purchase_request_id);
                    table.CheckConstraint("purchase_request_rejection_note", "\"request_status\" <> 'REJECTED' OR \"decision_note\" IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "purchase_request_item",
                columns: table => new
                {
                    purchase_request_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    estimated_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    item_note = table.Column<string>(type: "text", nullable: true),
                    shortage_override_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_request_item", x => x.purchase_request_item_id);
                    table.CheckConstraint("purchase_request_item_nonnegative_cost", "\"estimated_cost\" IS NULL OR \"estimated_cost\" >= 0");
                    table.CheckConstraint("purchase_request_item_positive_quantity", "\"requested_quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_purchase_request_item_material_material_id",
                        column: x => x.material_id,
                        principalTable: "material",
                        principalColumn: "material_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_request_item_purchase_request_purchase_request_id",
                        column: x => x.purchase_request_id,
                        principalTable: "purchase_request",
                        principalColumn: "purchase_request_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_request_state_change",
                columns: table => new
                {
                    purchase_request_state_change_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    change_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_request_state_change", x => x.purchase_request_state_change_id);
                    table.ForeignKey(
                        name: "FK_purchase_request_state_change_purchase_request_purchase_req~",
                        column: x => x.purchase_request_id,
                        principalTable: "purchase_request",
                        principalColumn: "purchase_request_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_request_work_area_status",
                table: "purchase_request",
                columns: new[] { "work_area_id", "request_status" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_request_item_material_id",
                table: "purchase_request_item",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ux_purchase_request_item_request_material",
                table: "purchase_request_item",
                columns: new[] { "purchase_request_id", "material_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_request_state_change_request",
                table: "purchase_request_state_change",
                columns: new[] { "purchase_request_id", "changed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_request_item");

            migrationBuilder.DropTable(
                name: "purchase_request_state_change");

            migrationBuilder.DropTable(
                name: "purchase_request");
        }
    }
}

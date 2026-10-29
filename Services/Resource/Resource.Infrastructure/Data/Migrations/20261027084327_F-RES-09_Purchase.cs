using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FRES09_Purchase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase",
                columns: table => new
                {
                    purchase_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    purchase_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cost_overrun_approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cost_overrun_approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    receipt_file_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    info_request_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase", x => x.purchase_id);
                    table.CheckConstraint("purchase_nonnegative_cost", "\"total_cost\" >= 0");
                    table.ForeignKey(
                        name: "FK_purchase_purchase_request_purchase_request_id",
                        column: x => x.purchase_request_id,
                        principalTable: "purchase_request",
                        principalColumn: "purchase_request_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_item",
                columns: table => new
                {
                    purchase_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_request_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchased_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    total_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    received_usable_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    received_unusable_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_item", x => x.purchase_item_id);
                    table.CheckConstraint("purchase_item_nonnegative_usable", "\"received_usable_quantity\" IS NULL OR \"received_usable_quantity\" >= 0");
                    table.CheckConstraint("purchase_item_valid_amounts", "\"purchased_quantity\" > 0 AND \"unit_price\" >= 0 AND \"total_price\" >= 0");
                    table.ForeignKey(
                        name: "FK_purchase_item_material_material_id",
                        column: x => x.material_id,
                        principalTable: "material",
                        principalColumn: "material_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_item_purchase_purchase_id",
                        column: x => x.purchase_id,
                        principalTable: "purchase",
                        principalColumn: "purchase_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_purchase_item_purchase_request_item_purchase_request_item_id",
                        column: x => x.purchase_request_item_id,
                        principalTable: "purchase_request_item",
                        principalColumn: "purchase_request_item_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_request",
                table: "purchase",
                column: "purchase_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_item_material_id",
                table: "purchase_item",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_item_purchase_request_item_id",
                table: "purchase_item",
                column: "purchase_request_item_id");

            migrationBuilder.CreateIndex(
                name: "ux_purchase_item_purchase_request_item",
                table: "purchase_item",
                columns: new[] { "purchase_id", "purchase_request_item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_item");

            migrationBuilder.DropTable(
                name: "purchase");
        }
    }
}

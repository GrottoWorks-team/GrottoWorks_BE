using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SelfPurchaseReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "self_purchase_receipt",
                columns: table => new
                {
                    self_purchase_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    invoice_file_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_self_purchase_receipt", x => x.self_purchase_receipt_id);
                    table.CheckConstraint("self_purchase_nonnegative_cost", "\"total_cost\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "self_purchase_item",
                columns: table => new
                {
                    self_purchase_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    self_purchase_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    total_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_self_purchase_item", x => x.self_purchase_item_id);
                    table.CheckConstraint("self_purchase_item_valid_amounts", "\"quantity\" > 0 AND \"unit_price\" >= 0 AND \"total_price\" >= 0");
                    table.ForeignKey(
                        name: "FK_self_purchase_item_material_material_id",
                        column: x => x.material_id,
                        principalTable: "material",
                        principalColumn: "material_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_self_purchase_item_self_purchase_receipt_self_purchase_rece~",
                        column: x => x.self_purchase_receipt_id,
                        principalTable: "self_purchase_receipt",
                        principalColumn: "self_purchase_receipt_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_self_purchase_item_material_id",
                table: "self_purchase_item",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ux_self_purchase_item_material",
                table: "self_purchase_item",
                columns: new[] { "self_purchase_receipt_id", "material_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_self_purchase_season_date",
                table: "self_purchase_receipt",
                columns: new[] { "season_id", "purchase_date" });

            migrationBuilder.CreateIndex(
                name: "ix_self_purchase_work_area_date",
                table: "self_purchase_receipt",
                columns: new[] { "work_area_id", "purchase_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "self_purchase_item");

            migrationBuilder.DropTable(
                name: "self_purchase_receipt");
        }
    }
}

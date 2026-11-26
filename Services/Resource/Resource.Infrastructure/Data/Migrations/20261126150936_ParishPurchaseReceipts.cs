using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ParishPurchaseReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parish_purchase_receipt",
                columns: table => new
                {
                    parish_purchase_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    invoice_file_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parish_purchase_receipt", x => x.parish_purchase_receipt_id);
                    table.CheckConstraint("parish_purchase_nonnegative_cost", "\"total_cost\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "parish_purchase_item",
                columns: table => new
                {
                    parish_purchase_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parish_purchase_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    total_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parish_purchase_item", x => x.parish_purchase_item_id);
                    table.CheckConstraint("parish_purchase_item_valid_amounts", "\"quantity\" > 0 AND \"unit_price\" >= 0 AND \"total_price\" >= 0");
                    table.ForeignKey(
                        name: "FK_parish_purchase_item_material_material_id",
                        column: x => x.material_id,
                        principalTable: "material",
                        principalColumn: "material_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_parish_purchase_item_parish_purchase_receipt_parish_purchas~",
                        column: x => x.parish_purchase_receipt_id,
                        principalTable: "parish_purchase_receipt",
                        principalColumn: "parish_purchase_receipt_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_parish_purchase_item_material_id",
                table: "parish_purchase_item",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ux_parish_purchase_item_material",
                table: "parish_purchase_item",
                columns: new[] { "parish_purchase_receipt_id", "material_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parish_purchase_parish_season_date",
                table: "parish_purchase_receipt",
                columns: new[] { "parish_id", "season_id", "purchase_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parish_purchase_item");

            migrationBuilder.DropTable(
                name: "parish_purchase_receipt");
        }
    }
}

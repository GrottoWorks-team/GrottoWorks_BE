using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FRES04_MaterialTransactionLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "material_transaction",
                columns: table => new
                {
                    material_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    transaction_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    transaction_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reference_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transaction_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_transaction", x => x.material_transaction_id);
                    table.CheckConstraint("material_transaction_quantity_non_zero", "\"transaction_quantity\" <> 0");
                    table.CheckConstraint("material_transaction_quantity_sign", "(\"transaction_type\" IN ('OPENING_STOCK','PURCHASE_DELIVERY','DONATION_RECEIPT','TASK_RETURN','TRANSFER_IN') AND \"transaction_quantity\" > 0) OR (\"transaction_type\" IN ('TRANSFER_OUT','ADJUSTMENT_DAMAGED','ADJUSTMENT_LOST','ADJUSTMENT_DISPOSED') AND \"transaction_quantity\" < 0)");
                    table.ForeignKey(
                        name: "FK_material_transaction_material_material_id",
                        column: x => x.material_id,
                        principalTable: "material",
                        principalColumn: "material_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_material_transaction_material_id",
                table: "material_transaction",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_transaction_work_area_material",
                table: "material_transaction",
                columns: new[] { "work_area_id", "material_id" });

            migrationBuilder.CreateIndex(
                name: "ux_material_transaction_opening_stock",
                table: "material_transaction",
                columns: new[] { "work_area_id", "material_id" },
                unique: true,
                filter: "\"transaction_type\" = 'OPENING_STOCK'");

            migrationBuilder.CreateIndex(
                name: "ux_material_transaction_reference",
                table: "material_transaction",
                columns: new[] { "reference_type", "reference_id", "transaction_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "material_transaction");
        }
    }
}

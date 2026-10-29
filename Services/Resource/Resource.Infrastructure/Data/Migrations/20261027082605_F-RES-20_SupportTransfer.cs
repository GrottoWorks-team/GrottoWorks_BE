using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FRES20_SupportTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "support_transfer",
                columns: table => new
                {
                    support_response_material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    transfer_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transferred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_usable_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    received_unusable_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    received_confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    transfer_note = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_transfer", x => x.support_response_material_id);
                    table.CheckConstraint("support_transfer_different_work_areas", "\"source_work_area_id\" <> \"target_work_area_id\"");
                    table.CheckConstraint("support_transfer_positive_quantity", "\"transfer_quantity\" > 0");
                    table.CheckConstraint("support_transfer_valid_receipt", "(\"received_usable_quantity\" IS NULL OR \"received_usable_quantity\" >= 0) AND (\"received_unusable_quantity\" IS NULL OR \"received_unusable_quantity\" >= 0)");
                    table.ForeignKey(
                        name: "FK_support_transfer_material_material_id",
                        column: x => x.material_id,
                        principalTable: "material",
                        principalColumn: "material_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_support_transfer_material_id",
                table: "support_transfer",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_transfer_target_status",
                table: "support_transfer",
                columns: new[] { "target_work_area_id", "transfer_status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_transfer");
        }
    }
}

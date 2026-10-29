using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FRES18_BorrowedItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "borrowed_item",
                columns: table => new
                {
                    borrowed_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    owner_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    owner_address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    owner_note = table.Column<string>(type: "text", nullable: true),
                    borrowed_item_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    borrowed_item_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    borrowed_item_description = table.Column<string>(type: "text", nullable: true),
                    initial_condition = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    expected_return_date = table.Column<DateOnly>(type: "date", nullable: false),
                    borrowed_item_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rejection_reason = table.Column<string>(type: "text", nullable: true),
                    last_reminded_on = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_borrowed_item", x => x.borrowed_item_id);
                    table.CheckConstraint("borrowed_item_positive_quantity", "\"borrowed_item_quantity\" > 0");
                    table.CheckConstraint("borrowed_item_rejection_reason", "\"borrowed_item_status\" <> 'REJECTED' OR \"rejection_reason\" IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "borrow_transaction",
                columns: table => new
                {
                    borrow_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    borrowed_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    borrow_date = table.Column<DateOnly>(type: "date", nullable: false),
                    return_date = table.Column<DateOnly>(type: "date", nullable: true),
                    return_condition = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    returned_quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transaction_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_borrow_transaction", x => x.borrow_transaction_id);
                    table.CheckConstraint("borrow_transaction_return_condition", "\"return_date\" IS NULL OR \"return_condition\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_borrow_transaction_borrowed_item_borrowed_item_id",
                        column: x => x.borrowed_item_id,
                        principalTable: "borrowed_item",
                        principalColumn: "borrowed_item_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_borrow_transaction_borrowed_item_id",
                table: "borrow_transaction",
                column: "borrowed_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_borrowed_item_season_status_due",
                table: "borrowed_item",
                columns: new[] { "season_id", "borrowed_item_status", "expected_return_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "borrow_transaction");

            migrationBuilder.DropTable(
                name: "borrowed_item");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmergencyPurchaseFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "self_purchase_receipt",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "MO_SELF_PURCHASE");

            migrationBuilder.AddColumn<Guid>(
                name: "task_id",
                table: "self_purchase_receipt",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_self_purchase_task",
                table: "self_purchase_receipt",
                column: "task_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_self_purchase_task",
                table: "self_purchase_receipt");

            migrationBuilder.DropColumn(
                name: "source",
                table: "self_purchase_receipt");

            migrationBuilder.DropColumn(
                name: "task_id",
                table: "self_purchase_receipt");
        }
    }
}

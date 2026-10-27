using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class R12_PurchaseRequestPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // R-12 (Spec v1.3.2, Contract v1.3.2): the preparation state is PLANNING, DRAFT no longer exists.
            migrationBuilder.Sql("UPDATE purchase_request SET request_status = 'PLANNING' WHERE request_status = 'DRAFT';");
            migrationBuilder.Sql("UPDATE purchase_request_state_change SET from_status = 'PLANNING' WHERE from_status = 'DRAFT';");
            migrationBuilder.Sql("UPDATE purchase_request_state_change SET to_status = 'PLANNING' WHERE to_status = 'DRAFT';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE purchase_request SET request_status = 'DRAFT' WHERE request_status = 'PLANNING';");
            migrationBuilder.Sql("UPDATE purchase_request_state_change SET from_status = 'DRAFT' WHERE from_status = 'PLANNING';");
            migrationBuilder.Sql("UPDATE purchase_request_state_change SET to_status = 'DRAFT' WHERE to_status = 'PLANNING';");
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Resource.Infrastructure.Inventory;

namespace Resource.Infrastructure.Data.Migrations;

[DbContext(typeof(ResourceDbContext))]
[Migration("20261103200000_AddSupportTransferOperations")]
public sealed class AddSupportTransferOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE support_transfer_operation (
            operation_id uuid PRIMARY KEY,
            support_response_material_id uuid NOT NULL,
            source_work_area_id uuid NOT NULL,
            target_work_area_id uuid NOT NULL,
            material_id uuid NOT NULL,
            quantity numeric(12,2) NOT NULL CHECK (quantity > 0),
            approved_by_user_id uuid NOT NULL,
            status varchar(20) NOT NULL CHECK (status IN ('COMPLETED','REJECTED')),
            error_code varchar(100),
            decided_at timestamp with time zone NOT NULL,
            CHECK ((status = 'REJECTED') = (error_code IS NOT NULL))
        );
        CREATE INDEX "IX_support_transfer_operation_support_response_material_id"
            ON support_transfer_operation(support_response_material_id);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("support_transfer_operation");
}

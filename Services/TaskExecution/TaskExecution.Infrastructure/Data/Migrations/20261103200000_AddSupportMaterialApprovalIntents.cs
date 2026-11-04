using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261103200000_AddSupportMaterialApprovalIntents")]
public sealed class AddSupportMaterialApprovalIntents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        ALTER TABLE support_response_material
          ADD COLUMN approval_operation_id uuid,
          ADD COLUMN approval_requested_quantity numeric(12,2),
          ADD COLUMN approval_requested_by uuid,
          ADD COLUMN approval_requested_at timestamp with time zone,
          ADD COLUMN approval_correlation_id varchar(200),
          ADD COLUMN approval_error_code varchar(100),
          ADD COLUMN review_reason varchar(500);
        CREATE INDEX ix_support_material_pending_approval ON support_response_material(support_response_material_id)
          WHERE material_status='PENDING' AND approval_operation_id IS NOT NULL AND approval_error_code IS NULL;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        DROP INDEX ix_support_material_pending_approval;
        ALTER TABLE support_response_material DROP COLUMN approval_operation_id,DROP COLUMN approval_requested_quantity,
          DROP COLUMN approval_requested_by,DROP COLUMN approval_requested_at,DROP COLUMN approval_correlation_id,
          DROP COLUMN approval_error_code,DROP COLUMN review_reason;
        """);
}

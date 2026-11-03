using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261103180000_AddSupportMaterialOffers")]
public sealed class AddSupportMaterialOffers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        ALTER TABLE support_request_material ADD CONSTRAINT ak_support_demand_request_id UNIQUE(support_request_id,support_request_material_id);
        CREATE TABLE support_response_material(
          support_response_material_id uuid PRIMARY KEY,support_response_id uuid NOT NULL,support_request_id uuid NOT NULL,
          support_request_material_id uuid NOT NULL,source_work_area_id uuid NOT NULL,material_id uuid NOT NULL,
          offered_quantity numeric(12,2) NOT NULL,approved_quantity numeric(12,2),received_usable_quantity numeric(12,2),received_unusable_quantity numeric(12,2),
          material_status varchar(30) NOT NULL,approved_by_user_id uuid,approved_at timestamptz,received_confirmed_by_user_id uuid,
          received_confirmed_at timestamptz,material_note varchar(2000),version integer NOT NULL DEFAULT 1,
          CONSTRAINT support_material_positive_offer CHECK(offered_quantity>0),
          CONSTRAINT fk_support_offer_response FOREIGN KEY(support_request_id,support_response_id)
            REFERENCES support_response(support_request_id,support_response_id) ON DELETE CASCADE,
          CONSTRAINT fk_support_offer_demand FOREIGN KEY(support_request_id,support_request_material_id)
            REFERENCES support_request_material(support_request_id,support_request_material_id) ON DELETE RESTRICT);
        CREATE INDEX ix_support_offer_demand ON support_response_material(support_request_id,support_request_material_id);
        CREATE INDEX ix_support_offer_response ON support_response_material(support_request_id,support_response_id);
        CREATE UNIQUE INDEX ux_support_offer_response_demand_source ON support_response_material(support_response_id,support_request_material_id,source_work_area_id);
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        DROP TABLE support_response_material;
        ALTER TABLE support_request_material DROP CONSTRAINT ak_support_demand_request_id;
        """);
}

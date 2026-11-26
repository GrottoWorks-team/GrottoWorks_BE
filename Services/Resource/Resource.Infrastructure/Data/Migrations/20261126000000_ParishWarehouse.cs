using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Data.Migrations;

/// <summary>
/// F-RES-21: parish warehouse ledger + two-step distributions. Also records the parish of a projected season, the
/// material officer who entered a purchase, and allows a MATERIAL donation without a work area (parish warehouse).
/// </summary>
[DbContext(typeof(ResourceDbContext))]
[Migration("20261126000000_ParishWarehouse")]
public sealed class ParishWarehouse : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE parish_material_transaction (
            parish_material_transaction_id uuid PRIMARY KEY,
            parish_id uuid NOT NULL,
            season_id uuid NOT NULL,
            material_id uuid NOT NULL REFERENCES material(material_id) ON DELETE RESTRICT,
            transaction_type character varying(40) NOT NULL,
            transaction_quantity numeric(12,2) NOT NULL,
            transaction_date timestamp with time zone NOT NULL,
            reference_type character varying(40),
            reference_id uuid,
            created_by_user_id uuid,
            transaction_note character varying(2000),
            CONSTRAINT parish_material_transaction_quantity_non_zero CHECK (transaction_quantity <> 0),
            CONSTRAINT parish_material_transaction_quantity_sign CHECK (
                (transaction_type IN ('OPENING_STOCK','PURCHASE_DELIVERY','DONATION_RECEIPT','DISTRIBUTION_RETURN') AND transaction_quantity > 0) OR
                (transaction_type IN ('DISTRIBUTION_OUT','ADJUSTMENT_DAMAGED','ADJUSTMENT_LOST','ADJUSTMENT_DISPOSED') AND transaction_quantity < 0))
        );
        CREATE INDEX ix_parish_material_transaction_parish_season_material
            ON parish_material_transaction (parish_id, season_id, material_id);
        CREATE UNIQUE INDEX ux_parish_material_transaction_reference
            ON parish_material_transaction (reference_type, reference_id, transaction_type);
        CREATE UNIQUE INDEX ux_parish_material_transaction_opening_stock
            ON parish_material_transaction (parish_id, season_id, material_id)
            WHERE transaction_type = 'OPENING_STOCK';

        CREATE TABLE parish_distribution (
            parish_distribution_id uuid PRIMARY KEY,
            parish_id uuid NOT NULL,
            season_id uuid NOT NULL,
            target_work_area_id uuid NOT NULL,
            distribution_status character varying(20) NOT NULL,
            distribution_origin character varying(20) NOT NULL,
            issued_by_user_id uuid NOT NULL,
            issued_at timestamp with time zone NOT NULL,
            received_confirmed_by_user_id uuid,
            received_confirmed_at timestamp with time zone,
            distribution_note character varying(2000),
            cancel_reason character varying(500)
        );
        CREATE INDEX ix_parish_distribution_scope
            ON parish_distribution (parish_id, season_id, target_work_area_id, distribution_status);
        CREATE INDEX ix_parish_distribution_target ON parish_distribution (target_work_area_id);

        CREATE TABLE parish_distribution_item (
            parish_distribution_item_id uuid PRIMARY KEY,
            parish_distribution_id uuid NOT NULL REFERENCES parish_distribution(parish_distribution_id) ON DELETE CASCADE,
            material_id uuid NOT NULL REFERENCES material(material_id) ON DELETE RESTRICT,
            issued_quantity numeric(12,2) NOT NULL,
            received_usable_quantity numeric(12,2),
            received_unusable_quantity numeric(12,2),
            CONSTRAINT parish_distribution_item_issued_positive CHECK (issued_quantity > 0),
            CONSTRAINT parish_distribution_item_receipt_valid CHECK (
                (received_usable_quantity IS NULL OR received_usable_quantity >= 0) AND
                (received_unusable_quantity IS NULL OR received_unusable_quantity >= 0))
        );
        CREATE UNIQUE INDEX ux_parish_distribution_item_material
            ON parish_distribution_item (parish_distribution_id, material_id);

        ALTER TABLE season_scope ADD COLUMN parish_id uuid;
        UPDATE season_scope s
        SET parish_id = (SELECT w.parish_id FROM work_area_scope w WHERE w.season_id = s.season_id LIMIT 1)
        WHERE s.parish_id IS NULL;

        ALTER TABLE purchase ADD COLUMN recorded_by_user_id uuid;

        ALTER TABLE donation DROP CONSTRAINT IF EXISTS donation_material_requires_work_area;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE donation ADD CONSTRAINT donation_material_requires_work_area
            CHECK (donation_type <> 'MATERIAL' OR work_area_id IS NOT NULL);
        ALTER TABLE purchase DROP COLUMN IF EXISTS recorded_by_user_id;
        ALTER TABLE season_scope DROP COLUMN IF EXISTS parish_id;
        DROP TABLE IF EXISTS parish_distribution_item;
        DROP TABLE IF EXISTS parish_distribution;
        DROP TABLE IF EXISTS parish_material_transaction;
        """);
}

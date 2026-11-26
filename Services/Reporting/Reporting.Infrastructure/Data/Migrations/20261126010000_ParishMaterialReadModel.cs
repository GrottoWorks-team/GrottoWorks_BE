using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Reporting.Infrastructure.Data.Migrations;

/// <summary>
/// 26/11/2026: parish central warehouse balance read model, fed by Resource's <c>parish.material.stock.changed</c> so the
/// materials report can show a "Kho tổng giáo xứ" row. Raw SQL keeps it next to the other hand-written migration
/// (<see cref="AddScopeReplicationInbox"/>).
/// </summary>
[DbContext(typeof(ReportingDbContext))]
[Migration("20261126010000_ParishMaterialReadModel")]
public sealed class ParishMaterialReadModel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE rm_parish_material_line (
            parish_id uuid NOT NULL,
            season_id uuid NOT NULL,
            material_id uuid NOT NULL,
            material_code character varying(50),
            material_name character varying(150),
            unit character varying(30),
            confirmed_available numeric(12,2) NOT NULL,
            updated_at timestamp with time zone NOT NULL,
            PRIMARY KEY (parish_id, season_id, material_id)
        );
        CREATE INDEX ix_rm_parish_material_line_parish_season
            ON rm_parish_material_line (parish_id, season_id);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("rm_parish_material_line");
}

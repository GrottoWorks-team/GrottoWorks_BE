using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ParishCoordination.Infrastructure.Data.Migrations;

[DbContext(typeof(ParishCoordinationDbContext))]
[Migration("20261126040000_AddWorkAreaMapPin")]
public sealed class AddWorkAreaMapPin : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>("map_x", "work_area", type: "numeric(8,7)", precision: 8, scale: 7, nullable: true);
        migrationBuilder.AddColumn<decimal>("map_y", "work_area", type: "numeric(8,7)", precision: 8, scale: 7, nullable: true);
        migrationBuilder.AddCheckConstraint("work_area_map_pin_valid", "work_area",
            "(map_x IS NULL AND map_y IS NULL) OR (map_x IS NOT NULL AND map_y IS NOT NULL AND map_x BETWEEN 0 AND 1 AND map_y BETWEEN 0 AND 1)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("work_area_map_pin_valid", "work_area");
        migrationBuilder.DropColumn("map_x", "work_area");
        migrationBuilder.DropColumn("map_y", "work_area");
    }
}

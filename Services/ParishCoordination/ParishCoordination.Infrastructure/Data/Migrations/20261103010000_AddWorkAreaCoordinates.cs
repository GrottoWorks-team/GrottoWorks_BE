using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ParishCoordination.Infrastructure.Data.Migrations;

[DbContext(typeof(ParishCoordinationDbContext))]
[Migration("20261103010000_AddWorkAreaCoordinates")]
public sealed class AddWorkAreaCoordinates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>("latitude", "work_area", type: "numeric(10,7)", precision: 10, scale: 7, nullable: true);
        migrationBuilder.AddColumn<decimal>("longitude", "work_area", type: "numeric(10,7)", precision: 10, scale: 7, nullable: true);
        migrationBuilder.AddCheckConstraint("work_area_latitude_range", "work_area", "latitude IS NULL OR latitude BETWEEN -90 AND 90");
        migrationBuilder.AddCheckConstraint("work_area_longitude_range", "work_area", "longitude IS NULL OR longitude BETWEEN -180 AND 180");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("work_area_latitude_range", "work_area");
        migrationBuilder.DropCheckConstraint("work_area_longitude_range", "work_area");
        migrationBuilder.DropColumn("latitude", "work_area");
        migrationBuilder.DropColumn("longitude", "work_area");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        new ParishCoordinationDbContextModelSnapshot().BuildSnapshot(modelBuilder);
}

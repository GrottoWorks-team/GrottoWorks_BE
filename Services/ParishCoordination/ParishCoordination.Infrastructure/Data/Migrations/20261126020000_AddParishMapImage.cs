using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParishCoordination.Infrastructure.Data.Migrations;

[Migration("20261126020000_AddParishMapImage")]
public partial class AddParishMapImage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "map_image_file_id",
            table: "parish",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "map_image_file_name",
            table: "parish",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "map_image_file_name", table: "parish");
        migrationBuilder.DropColumn(name: "map_image_file_id", table: "parish");
    }
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParishCoordination.Infrastructure.Data.Migrations;

[DbContext(typeof(ParishCoordinationDbContext))]
[Migration("20261126030000_AddParishMapAttachmentIntent")]
public partial class AddParishMapAttachmentIntent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "parish_map_attachment_intent",
            columns: table => new
            {
                reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                parish_id = table.Column<Guid>(type: "uuid", nullable: false),
                file_id = table.Column<Guid>(type: "uuid", nullable: false),
                map_saved = table.Column<bool>(type: "boolean", nullable: false),
                next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_parish_map_attachment_intent", row => row.reservation_id));

        migrationBuilder.CreateIndex("IX_parish_map_attachment_intent_next_attempt_at", "parish_map_attachment_intent", "next_attempt_at");
        migrationBuilder.CreateIndex("IX_parish_map_attachment_intent_parish_id_file_id", "parish_map_attachment_intent",
            new[] { "parish_id", "file_id" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("parish_map_attachment_intent");
}

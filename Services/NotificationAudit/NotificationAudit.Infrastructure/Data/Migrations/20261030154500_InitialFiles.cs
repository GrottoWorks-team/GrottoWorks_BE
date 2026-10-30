using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace NotificationAudit.Infrastructure.Data.Migrations;

[DbContext(typeof(NotificationAuditDbContext))]
[Migration("20261030154500_InitialFiles")]
public partial class InitialFiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "file_object",
            columns: table => new
            {
                file_id = table.Column<Guid>(type: "uuid", nullable: false),
                uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                object_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false),
                checksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                reference_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("pk_file_object", x => x.file_id));

        migrationBuilder.CreateIndex(
            name: "ix_file_object_uploader_status",
            table: "file_object",
            columns: new[] { "uploaded_by_user_id", "status" });
        migrationBuilder.CreateIndex(
            name: "ux_file_object_object_key",
            table: "file_object",
            column: "object_key",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "file_object");
}

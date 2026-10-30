using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationAudit.Infrastructure.Data.Migrations;

[DbContext(typeof(NotificationAuditDbContext))]
[Migration("20261030160000_AddNotifications")]
public partial class AddNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notification",
            columns: table => new
            {
                notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                body = table.Column<string>(type: "text", nullable: false),
                reference_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                is_read = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("pk_notification", x => x.notification_id));
        migrationBuilder.CreateIndex("ix_notification_recipient_created_at", "notification", new[] { "recipient_user_id", "created_at" });
        migrationBuilder.CreateIndex("ix_notification_recipient_is_read", "notification", new[] { "recipient_user_id", "is_read" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("notification");
}

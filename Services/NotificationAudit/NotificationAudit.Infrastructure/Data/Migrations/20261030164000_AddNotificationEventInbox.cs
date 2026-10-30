using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationAudit.Infrastructure.Data.Migrations;

[DbContext(typeof(NotificationAuditDbContext))]
[Migration("20261030164000_AddNotificationEventInbox")]
public partial class AddNotificationEventInbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notification_event_inbox",
            columns: table => new
            {
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table => table.PrimaryKey("pk_notification_event_inbox", x => x.event_id));
        migrationBuilder.CreateIndex(
            "ix_notification_event_inbox_status_received_at",
            "notification_event_inbox",
            new[] { "status", "received_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("notification_event_inbox");
}

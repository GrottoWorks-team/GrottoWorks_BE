using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationAudit.Infrastructure.Data.Migrations;

[DbContext(typeof(NotificationAuditDbContext))]
[Migration("20261030161000_AddNotificationPreferences")]
public partial class AddNotificationPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notification_preference",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                in_app_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                push_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                email_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("pk_notification_preference", x => x.user_id));
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("notification_preference");
}

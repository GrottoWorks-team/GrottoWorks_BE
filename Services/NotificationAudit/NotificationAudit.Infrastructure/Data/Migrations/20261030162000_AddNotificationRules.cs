using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationAudit.Infrastructure.Data.Migrations;

[DbContext(typeof(NotificationAuditDbContext))]
[Migration("20261030162000_AddNotificationRules")]
public partial class AddNotificationRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notification_rule",
            columns: table => new
            {
                notification_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                in_app_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("pk_notification_rule", x => x.notification_rule_id));
        migrationBuilder.CreateIndex("ux_notification_rule_event_type", "notification_rule", "event_type", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("notification_rule");
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NotificationAudit.Infrastructure.Data.Migrations;

[DbContext(typeof(NotificationAuditDbContext))]
[Migration("20261103140000_AddRetainedEventContext")]
public sealed class AddRetainedEventContext : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "safe_envelope", table: "notification_event_inbox", type: "jsonb", nullable: true);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn("safe_envelope", "notification_event_inbox");
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationAudit.Infrastructure.Data.Migrations;

[DbContext(typeof(NotificationAuditDbContext))]
[Migration("20261030163000_AddActivityLogs")]
public partial class AddActivityLogs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "activity_log",
            columns: table => new
            {
                activity_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                parish_id = table.Column<Guid>(type: "uuid", nullable: true),
                season_id = table.Column<Guid>(type: "uuid", nullable: true),
                action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                old_value = table.Column<string>(type: "jsonb", nullable: true),
                new_value = table.Column<string>(type: "jsonb", nullable: true),
                reason = table.Column<string>(type: "text", nullable: true),
                correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("pk_activity_log", x => x.activity_log_id));
        migrationBuilder.CreateIndex("ix_activity_log_actor_created_at", "activity_log", new[] { "actor_user_id", "created_at" });
        migrationBuilder.CreateIndex("ix_activity_log_entity_created_at", "activity_log", new[] { "entity_type", "entity_id", "created_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("activity_log");
}

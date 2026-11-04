using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NotificationAudit.Infrastructure.Data.Migrations;

[DbContext(typeof(NotificationAuditDbContext))]
[Migration("20261103150000_AlignNotificationRuleNames")]
public sealed class AlignNotificationRuleNames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM notification_rule WHERE event_type = 'purchase.request.decided'
            AND notification_rule_id <> '71000000-0000-0000-0000-000000000011') OR
             EXISTS (SELECT 1 FROM notification_rule WHERE event_type = 'readiness.area.ready'
            AND notification_rule_id <> '71000000-0000-0000-0000-000000000012') THEN
            RAISE EXCEPTION 'Conflicting canonical notification rules. Reconcile them to the documented stable IDs before migrating.';
          END IF;
        END $$;
        UPDATE notification_rule SET event_type = 'purchase.request.decided', version = version + 1, updated_at = now()
          WHERE notification_rule_id = '71000000-0000-0000-0000-000000000011' AND event_type = 'resource.purchase-request.decided';
        UPDATE notification_rule SET event_type = 'readiness.area.ready', version = version + 1, updated_at = now()
          WHERE notification_rule_id = '71000000-0000-0000-0000-000000000012' AND event_type = 'readiness.inspection.completed';
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE notification_rule SET event_type = 'resource.purchase-request.decided', version = version + 1, updated_at = now()
          WHERE notification_rule_id = '71000000-0000-0000-0000-000000000011' AND event_type = 'purchase.request.decided';
        UPDATE notification_rule SET event_type = 'readiness.inspection.completed', version = version + 1, updated_at = now()
          WHERE notification_rule_id = '71000000-0000-0000-0000-000000000012' AND event_type = 'readiness.area.ready';
        """);
}

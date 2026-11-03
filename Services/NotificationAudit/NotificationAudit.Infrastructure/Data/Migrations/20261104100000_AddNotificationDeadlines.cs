using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NotificationAudit.Infrastructure.Data.Migrations;

[DbContext(typeof(NotificationAuditDbContext))]
[Migration("20261104100000_AddNotificationDeadlines")]
public sealed class AddNotificationDeadlines : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        CREATE TABLE notification_task_deadline ("TaskId" uuid PRIMARY KEY,"ParishId" uuid NOT NULL,"SeasonId" uuid NOT NULL,
          "SourceVersion" bigint NOT NULL,"Deadline" timestamptz,"DeadlineGeneration" bigint NOT NULL,"Status" varchar(30) NOT NULL,"Assignees" uuid[] NOT NULL);
        CREATE INDEX "IX_notification_task_deadline_Deadline" ON notification_task_deadline("Deadline");
        CREATE TABLE notification_deadline_delivery ("TaskId" uuid NOT NULL,"DeadlineGeneration" bigint NOT NULL,"RecipientId" uuid NOT NULL,
          "Kind" varchar(20) NOT NULL,"NotificationId" uuid NOT NULL,"CreatedAt" timestamptz NOT NULL,
          PRIMARY KEY("TaskId","DeadlineGeneration","RecipientId","Kind"));
        CREATE TABLE notification_snapshot_receipt ("EventId" uuid PRIMARY KEY,"SourceKey" varchar(150) NOT NULL,"Fingerprint" varchar(64) NOT NULL,"ReceivedAt" timestamptz NOT NULL);
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        DROP TABLE notification_deadline_delivery,notification_task_deadline,notification_snapshot_receipt;
        """);
}

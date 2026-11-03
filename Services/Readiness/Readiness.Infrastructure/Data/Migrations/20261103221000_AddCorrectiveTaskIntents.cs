using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Readiness.Infrastructure.Data.Migrations;

[DbContext(typeof(ReadinessDbContext))]
[Migration("20261103221000_AddCorrectiveTaskIntents")]
public sealed class AddCorrectiveTaskIntents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE corrective_task_intent (
            "Id" uuid PRIMARY KEY, "CheckId" uuid NOT NULL, "ItemId" uuid NOT NULL, "WorkAreaId" uuid NOT NULL,
            "RequestedByUserId" uuid NOT NULL, "Payload" jsonb NOT NULL, "Fingerprint" varchar(64) NOT NULL,
            "TaskId" uuid NULL, "State" varchar(20) NOT NULL, "Attempts" integer NOT NULL,
            "NextAttemptAt" timestamptz NOT NULL, "CreatedAt" timestamptz NOT NULL);
        CREATE INDEX "IX_corrective_task_intent_State_NextAttemptAt" ON corrective_task_intent ("State", "NextAttemptAt");
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("corrective_task_intent");
}

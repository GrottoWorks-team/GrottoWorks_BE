using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NotificationAudit.Infrastructure.Data.Migrations;

[DbContext(typeof(NotificationAuditDbContext))]
[Migration("20261104090000_AddSystemJobs")]
public sealed class AddSystemJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE system_job (
                "id" uuid NOT NULL PRIMARY KEY,
                "owner_user_id" uuid NOT NULL,
                "kind" character varying(20) NOT NULL,
                "idempotency_key" uuid NOT NULL,
                "fingerprint" character varying(100) NOT NULL,
                "services_json" jsonb NOT NULL,
                "status" character varying(20) NOT NULL,
                "created_at" timestamp with time zone NOT NULL,
                "next_attempt_at" timestamp with time zone NOT NULL,
                "started_at" timestamp with time zone,
                "finished_at" timestamp with time zone,
                "attempts" integer NOT NULL,
                "lease_token" uuid,
                "lease_until" timestamp with time zone,
                "artifact_file_id" uuid,
                "artifact_object_key" character varying(500),
                "artifact_expires_at" timestamp with time zone,
                "error_code" character varying(100),
                "manifest_json" jsonb,
                "correlation_id" uuid
        );
        CREATE UNIQUE INDEX ux_system_job_owner_kind_key ON system_job (owner_user_id, kind, idempotency_key);
        CREATE INDEX ix_system_job_claim ON system_job (kind, status, next_attempt_at);
        CREATE UNIQUE INDEX "IX_system_job_artifact_file_id" ON system_job (artifact_file_id);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("system_job");
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Data.Migrations;

[DbContext(typeof(ResourceDbContext))]
[Migration("20261103220000_AddAttachmentIntents")]
public sealed class AddAttachmentIntents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE file_attachment_intent (
            "Id" uuid PRIMARY KEY, "FileId" uuid NOT NULL, "ReferenceId" uuid NOT NULL,
            "UploadedByUserId" uuid NOT NULL, "Purpose" varchar(50) NOT NULL,
            "State" varchar(20) NOT NULL, "Attempts" integer NOT NULL,
            "NextAttemptAt" timestamptz NOT NULL, "CreatedAt" timestamptz NOT NULL);
        CREATE UNIQUE INDEX "IX_file_attachment_intent_ReferenceId_FileId" ON file_attachment_intent ("ReferenceId", "FileId");
        CREATE INDEX "IX_file_attachment_intent_State_NextAttemptAt" ON file_attachment_intent ("State", "NextAttemptAt");
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("file_attachment_intent");
}

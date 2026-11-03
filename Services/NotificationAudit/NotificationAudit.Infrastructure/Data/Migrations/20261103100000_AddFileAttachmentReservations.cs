using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NotificationAudit.Infrastructure.Data.Migrations;

[DbContext(typeof(NotificationAuditDbContext))]
[Migration("20261103100000_AddFileAttachmentReservations")]
public sealed class AddFileAttachmentReservations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE file_object ADD attachment_reservation_id uuid,
          ADD attachment_service varchar(50), ADD reserved_reference_type varchar(50), ADD reserved_reference_id uuid;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE file_object DROP COLUMN attachment_reservation_id, DROP COLUMN attachment_service,
          DROP COLUMN reserved_reference_type, DROP COLUMN reserved_reference_id;
        """);
}

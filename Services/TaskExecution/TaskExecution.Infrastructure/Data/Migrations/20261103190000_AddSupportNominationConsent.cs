using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261103190000_AddSupportNominationConsent")]
public sealed class AddSupportNominationConsent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        ALTER TABLE support_response_member ADD COLUMN requires_confirmation boolean NOT NULL DEFAULT false,
          ADD COLUMN participation_accepted boolean NOT NULL DEFAULT false, ADD COLUMN participation_reason varchar(500);
        UPDATE support_response_member m SET requires_confirmation=(a.assignment_status='PENDING'),
          participation_accepted=(a.assignment_status IN ('ACCEPTED','IN_PROGRESS','COMPLETED'))
          FROM task_assignment a WHERE a.task_assignment_id=m.task_assignment_id AND m.member_status IN ('APPROVED','COMPLETED');
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        ALTER TABLE support_response_member DROP COLUMN requires_confirmation,DROP COLUMN participation_accepted,DROP COLUMN participation_reason;
        """);
}

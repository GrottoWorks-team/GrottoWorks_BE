using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261103170000_AddStaffingSupportResponses")]
public sealed class AddStaffingSupportResponses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        ALTER TABLE support_request_position ADD CONSTRAINT ak_support_position_request_id UNIQUE(support_request_id,support_request_position_id);
        CREATE TABLE support_response(
          support_response_id uuid PRIMARY KEY, support_request_id uuid NOT NULL,
          submitted_by_user_id uuid NOT NULL, volunteer_user_id uuid, responding_community_id uuid,
          response_status varchar(30) NOT NULL, response_quantity integer NOT NULL, response_note varchar(2000),
          created_at timestamptz NOT NULL, version integer NOT NULL DEFAULT 1,
          CONSTRAINT ak_support_response_request_id UNIQUE(support_request_id,support_response_id),
          CONSTRAINT fk_support_response_request FOREIGN KEY(support_request_id) REFERENCES support_request(support_request_id) ON DELETE CASCADE);
        CREATE INDEX ix_support_response_request_created ON support_response(support_request_id,created_at,support_response_id);
        CREATE TABLE support_response_member(
          support_response_member_id uuid PRIMARY KEY, support_response_id uuid NOT NULL, support_request_id uuid NOT NULL,
          support_request_position_id uuid NOT NULL, volunteer_user_id uuid NOT NULL, member_status varchar(30) NOT NULL,
          task_assignment_id uuid, version integer NOT NULL DEFAULT 1, reviewed_by_user_id uuid, reviewed_at timestamptz, review_reason varchar(500),
          CONSTRAINT fk_support_member_response FOREIGN KEY(support_request_id,support_response_id)
            REFERENCES support_response(support_request_id,support_response_id) ON DELETE CASCADE,
          CONSTRAINT fk_support_member_position FOREIGN KEY(support_request_id,support_request_position_id)
            REFERENCES support_request_position(support_request_id,support_request_position_id) ON DELETE RESTRICT,
          CONSTRAINT fk_support_member_assignment FOREIGN KEY(task_assignment_id) REFERENCES task_assignment(task_assignment_id) ON DELETE RESTRICT);
        CREATE UNIQUE INDEX ux_support_member_response_volunteer ON support_response_member(support_response_id,volunteer_user_id);
        CREATE UNIQUE INDEX ux_support_member_request_volunteer_active ON support_response_member(support_request_id,volunteer_user_id)
          WHERE member_status IN ('PENDING','APPROVED','COMPLETED');
        CREATE UNIQUE INDEX ux_support_member_assignment ON support_response_member(task_assignment_id);
        CREATE INDEX ix_support_member_response ON support_response_member(support_request_id,support_response_id);
        CREATE INDEX ix_support_member_position ON support_response_member(support_request_id,support_request_position_id);
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        DROP TABLE support_response_member;
        DROP TABLE support_response;
        ALTER TABLE support_request_position DROP CONSTRAINT ak_support_position_request_id;
        """);
}

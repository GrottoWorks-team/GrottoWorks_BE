using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;
[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261030090000_AddSupportRequests")]
public sealed class AddSupportRequests : Migration
{
    protected override void Up(MigrationBuilder b)=>b.Sql("""
        CREATE TABLE support_request (
          support_request_id uuid PRIMARY KEY, season_id uuid NOT NULL, work_area_id uuid NOT NULL,
          requested_by_user_id uuid NOT NULL, support_type varchar(30) NOT NULL, request_description varchar(2000),
          deadline date, request_status varchar(30) NOT NULL, created_at timestamptz NOT NULL, updated_at timestamptz,
          version integer NOT NULL DEFAULT 1);
        CREATE INDEX "IX_support_request_season_id_work_area_id" ON support_request(season_id,work_area_id);
        CREATE TABLE support_request_position (
          support_request_position_id uuid PRIMARY KEY,
          support_request_id uuid NOT NULL REFERENCES support_request(support_request_id) ON DELETE CASCADE,
          position_name varchar(150) NOT NULL, required_volunteer_count integer NOT NULL, position_note varchar(2000),
          CONSTRAINT support_position_positive_count CHECK(required_volunteer_count>0));
        CREATE INDEX "IX_support_request_position_support_request_id" ON support_request_position(support_request_id);
        CREATE TABLE support_position_skill (
          support_request_position_id uuid NOT NULL REFERENCES support_request_position(support_request_position_id) ON DELETE CASCADE,
          skill_id uuid NOT NULL, required_skill_level varchar(30), PRIMARY KEY(support_request_position_id,skill_id));
        CREATE TABLE support_request_material (
          support_request_material_id uuid PRIMARY KEY,
          support_request_id uuid NOT NULL REFERENCES support_request(support_request_id) ON DELETE CASCADE,
          material_id uuid NOT NULL, required_quantity numeric(12,2) NOT NULL, material_note varchar(2000),
          CONSTRAINT support_request_material_positive_quantity CHECK(required_quantity>0));
        CREATE UNIQUE INDEX "IX_support_request_material_support_request_id_material_id" ON support_request_material(support_request_id,material_id);
        """);
    protected override void Down(MigrationBuilder b)=>b.Sql("DROP TABLE support_position_skill; DROP TABLE support_request_material; DROP TABLE support_request_position; DROP TABLE support_request;");
}

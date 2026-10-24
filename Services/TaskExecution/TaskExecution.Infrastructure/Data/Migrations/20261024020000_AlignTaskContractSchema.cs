using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskExecution.Infrastructure.Data.Migrations;

public partial class AlignTaskContractSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM task
                    WHERE estimated_hours IS NULL
                       OR estimated_hours <= 0
                       OR estimated_hours * 60 > 599999
                       OR deadline IS NULL
                       OR required_volunteer_count <= 0
                       OR char_length(task_name) > 160
                       OR char_length(task_description) > 2000
                       OR char_length(completion_criteria) > 4000
                ) THEN
                    RAISE EXCEPTION 'Cannot align task contract schema: existing tasks must have a valid estimate, deadline, volunteer count and contract-compliant text lengths.';
                END IF;
            END $$;
            """);

        migrationBuilder.RenameColumn(
            name: "required_volunteer_count",
            table: "task",
            newName: "required_volunteers");

        migrationBuilder.RenameColumn(
            name: "start_date",
            table: "task",
            newName: "start_at");

        migrationBuilder.RenameColumn(
            name: "estimated_hours",
            table: "task",
            newName: "estimated_minutes");

        migrationBuilder.Sql(
            """
            ALTER TABLE task
            ALTER COLUMN estimated_minutes TYPE integer
            USING ROUND(estimated_minutes * 60)::integer;

            ALTER TABLE task
            ALTER COLUMN estimated_minutes SET NOT NULL;

            ALTER TABLE task
            ALTER COLUMN deadline SET NOT NULL;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "task_name",
            table: "task",
            type: "character varying(160)",
            maxLength: 160,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(200)",
            oldMaxLength: 200);

        migrationBuilder.AlterColumn<string>(
            name: "task_description",
            table: "task",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "completion_criteria",
            table: "task",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_task_positive_estimate",
            table: "task",
            sql: "estimated_minutes > 0");

        migrationBuilder.AddCheckConstraint(
            name: "ck_task_positive_volunteer_count",
            table: "task",
            sql: "required_volunteers > 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_task_positive_estimate",
            table: "task");

        migrationBuilder.DropCheckConstraint(
            name: "ck_task_positive_volunteer_count",
            table: "task");

        migrationBuilder.AlterColumn<string>(
            name: "task_name",
            table: "task",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(160)",
            oldMaxLength: 160);

        migrationBuilder.AlterColumn<string>(
            name: "task_description",
            table: "task",
            type: "text",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(2000)",
            oldMaxLength: 2000,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "completion_criteria",
            table: "task",
            type: "text",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(4000)",
            oldMaxLength: 4000,
            oldNullable: true);

        migrationBuilder.Sql(
            """
            ALTER TABLE task
            ALTER COLUMN deadline DROP NOT NULL;

            ALTER TABLE task
            ALTER COLUMN estimated_minutes TYPE numeric(6,2)
            USING estimated_minutes / 60.0;

            ALTER TABLE task
            ALTER COLUMN estimated_minutes DROP NOT NULL;
            """);

        migrationBuilder.RenameColumn(
            name: "estimated_minutes",
            table: "task",
            newName: "estimated_hours");

        migrationBuilder.RenameColumn(
            name: "required_volunteers",
            table: "task",
            newName: "required_volunteer_count");

        migrationBuilder.RenameColumn(
            name: "start_at",
            table: "task",
            newName: "start_date");
    }
}

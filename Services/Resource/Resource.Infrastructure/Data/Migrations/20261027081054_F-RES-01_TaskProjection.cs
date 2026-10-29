using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FRES01_TaskProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_projection",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    task_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    task_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    purchase_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignee_user_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    source_version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_projection", x => x.task_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_projection_purchase_request",
                table: "task_projection",
                column: "purchase_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_projection_work_area",
                table: "task_projection",
                column: "work_area_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_projection");
        }
    }
}

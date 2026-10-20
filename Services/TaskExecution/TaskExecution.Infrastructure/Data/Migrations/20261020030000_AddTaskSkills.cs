using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskExecution.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_skill",
                columns: table => new
                {
                    task_skill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    required_skill_level = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    required_volunteer_count = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_skill", x => x.task_skill_id);
                    table.ForeignKey(
                        name: "FK_task_skill_task_task_id",
                        column: x => x.task_id,
                        principalTable: "task",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_task_skill_task_id_skill_id",
                table: "task_skill",
                columns: new[] { "task_id", "skill_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_skill");
        }
    }
}

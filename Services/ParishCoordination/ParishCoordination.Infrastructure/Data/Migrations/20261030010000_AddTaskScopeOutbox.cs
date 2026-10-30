using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ParishCoordination.Infrastructure.TaskScopeSync;

#nullable disable
namespace ParishCoordination.Infrastructure.Data.Migrations;

[DbContext(typeof(ParishCoordinationDbContext))]
[Migration("20261030010000_AddTaskScopeOutbox")]
public class AddTaskScopeOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("task_scope_outbox", columns: table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false),
            scope_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
            aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
            source_version = table.Column<long>(type: "bigint", nullable: false),
            payload = table.Column<string>(type: "jsonb", nullable: false),
            created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            attempts = table.Column<int>(type: "integer", nullable: false),
            next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
        }, constraints: table => table.PrimaryKey("PK_task_scope_outbox", row => row.id));
        migrationBuilder.CreateIndex("ux_task_scope_outbox_version", "task_scope_outbox",
            new[] { "scope_type", "aggregate_id", "source_version" }, unique: true);
        migrationBuilder.CreateIndex("ix_task_scope_outbox_pending", "task_scope_outbox", "next_attempt_at",
            filter: "delivered_at IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("task_scope_outbox");

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        new ParishCoordinationDbContextModelSnapshot().BuildSnapshot(modelBuilder);
}

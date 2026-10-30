using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace TaskExecution.Infrastructure.Data.Migrations;
[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261030040000_AddAssignmentResponse")]
public sealed class AddAssignmentResponse : Migration
{
    protected override void Up(MigrationBuilder builder) => builder.Sql("ALTER TABLE task_assignment ADD COLUMN response_reason varchar(500);");
    protected override void Down(MigrationBuilder builder) => builder.DropColumn("response_reason","task_assignment");
}

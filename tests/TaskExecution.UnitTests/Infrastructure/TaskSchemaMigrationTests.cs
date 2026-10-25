using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TaskExecution.Infrastructure.Data.Migrations;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class TaskSchemaMigrationTests
{
    [Fact]
    public void Migration_guards_legacy_data_and_renames_contract_columns()
    {
        var migration = new AlignTaskContractSchema();
        var sql = string.Join(
            Environment.NewLine,
            migration.UpOperations.OfType<SqlOperation>().Select(operation => operation.Sql));
        var renamedColumns = migration.UpOperations
            .OfType<RenameColumnOperation>()
            .Select(operation => (operation.Name, operation.NewName));

        sql.Should().Contain("estimated_hours IS NULL");
        sql.Should().Contain("deadline IS NULL");
        sql.Should().Contain("ALTER COLUMN estimated_minutes TYPE integer");
        renamedColumns.Should().Contain(("estimated_hours", "estimated_minutes"));
        renamedColumns.Should().Contain(("required_volunteer_count", "required_volunteers"));
        renamedColumns.Should().Contain(("start_date", "start_at"));
    }
}

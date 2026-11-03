using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TaskExecution.Infrastructure.Data.Migrations;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class TaskSchemaMigrationTests
{
    [Fact]
    public void Evidence_index_alignment_preserves_existing_rows()
    {
        var operations = new AlignEvidenceIndexes().UpOperations;
        var index = operations.Should().ContainSingle().Subject.Should().BeOfType<CreateIndexOperation>().Subject;
        index.Table.Should().Be("progress_evidence");
        index.Columns.Should().Equal("task_progress_id");
    }
    [Fact]
    public void Historical_migration_designers_can_still_build_their_target_models()
    {
        var scopeModel = new AddTaskScopeProjection().TargetModel;
        scopeModel.GetEntityTypes().Select(entity => entity.GetTableName())
            .Should().BeEquivalentTo("season_scope", "work_area_scope");

        foreach (var model in new[] { new AddTask().TargetModel, new AlignTaskContractSchema().TargetModel })
        {
            model.FindEntityType("TaskExecution.Domain.Tasks.WorkTask")!
                .FindProperty("Priority")!.ClrType.Should().Be(typeof(string));
            model.GetEntityTypes().Should().Contain(entity => entity.GetTableName() == "task");
        }
    }

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

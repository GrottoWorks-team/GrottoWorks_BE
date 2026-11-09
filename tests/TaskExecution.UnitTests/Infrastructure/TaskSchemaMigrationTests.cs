using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TaskExecution.Infrastructure.Data;
using TaskExecution.Infrastructure.Data.Migrations;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class TaskSchemaMigrationTests
{
    [Fact]
    public void Team_leader_migration_leaves_old_rows_null_and_references_the_same_task()
    {
        var operations = new AddTaskTeamLeaderAssignment().UpOperations;
        var column = operations.OfType<AddColumnOperation>().Should().ContainSingle().Subject;
        column.Name.Should().Be("team_leader_assignment_id");
        column.IsNullable.Should().BeTrue();
        column.DefaultValue.Should().BeNull();
        var key = operations.OfType<AddUniqueConstraintOperation>().Should().ContainSingle().Subject;
        key.Table.Should().Be("task_assignment");
        key.Columns.Should().Equal("task_id", "task_assignment_id");
        var foreignKey = operations.OfType<AddForeignKeyOperation>().Should().ContainSingle().Subject;
        foreignKey.Columns.Should().Equal("task_id", "team_leader_assignment_id");
        foreignKey.PrincipalTable.Should().Be(key.Table);
        foreignKey.PrincipalColumns.Should().Equal(key.Columns);
        operations.Select(operation => operation.GetType()).Should().Equal(
            typeof(AddColumnOperation), typeof(AddUniqueConstraintOperation),
            typeof(CreateIndexOperation), typeof(AddForeignKeyOperation));
    }

    [Fact]
    public void Team_leader_model_has_the_optional_composite_foreign_key_and_a_matching_snapshot()
    {
        using var db = new TaskExecutionDbContext(new DbContextOptionsBuilder<TaskExecutionDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        var foreignKey = db.Model.FindEntityType(typeof(TaskExecution.Domain.Tasks.WorkTask))!.GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(TaskExecution.Domain.Assignments.TaskAssignment));
        foreignKey.IsRequired.Should().BeFalse();
        foreignKey.Properties.Select(property => property.Name).Should().Equal("Id", "TeamLeaderAssignmentId");
        foreignKey.PrincipalKey.Properties.Select(property => property.Name).Should().Equal("TaskId", "Id");
        db.Database.HasPendingModelChanges().Should().BeFalse();
    }
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

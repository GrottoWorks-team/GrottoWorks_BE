using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TaskExecution.Domain.Tasks;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class TaskModelTests
{
    [Fact]
    public void Task_mapping_uses_owned_table_and_concurrency_token()
    {
        var options = new DbContextOptionsBuilder<TaskExecutionDbContext>()
            .UseNpgsql("Host=localhost;Database=task_model_test;Username=test;Password=test")
            .Options;
        using var dbContext = new TaskExecutionDbContext(options);

        var entity = dbContext.Model.FindEntityType(typeof(WorkTask))!;
        var table = StoreObjectIdentifier.Table("task", null);

        entity.GetTableName().Should().Be("task");
        entity.FindProperty(nameof(WorkTask.Name))!.GetMaxLength().Should().Be(160);
        entity.FindProperty(nameof(WorkTask.Description))!.GetMaxLength().Should().Be(2000);
        entity.FindProperty(nameof(WorkTask.CompletionCriteria))!.GetMaxLength().Should().Be(4000);
        entity.FindProperty(nameof(WorkTask.EstimatedMinutes))!.GetColumnName(table)
            .Should().Be("estimated_minutes");
        entity.FindProperty(nameof(WorkTask.EstimatedMinutes))!.IsNullable.Should().BeFalse();
        entity.FindProperty(nameof(WorkTask.RequiredVolunteerCount))!.GetColumnName(table)
            .Should().Be("required_volunteers");
        entity.FindProperty(nameof(WorkTask.StartAt))!.GetColumnName(table).Should().Be("start_at");
        entity.FindProperty(nameof(WorkTask.Deadline))!.IsNullable.Should().BeFalse();
        entity.FindProperty(nameof(WorkTask.Version))!.IsConcurrencyToken.Should().BeTrue();
        entity.GetCheckConstraints().Select(constraint => constraint.Name).Should().Contain(
            "ck_task_positive_estimate",
            "ck_task_positive_volunteer_count");
        entity.GetForeignKeys().Should().BeEmpty("TaskExecution stores external ownership as IDs only");
    }

    [Fact]
    public void Task_skill_mapping_uses_expected_table_columns_and_unique_index()
    {
        var options = new DbContextOptionsBuilder<TaskExecutionDbContext>()
            .UseNpgsql("Host=localhost;Database=task_skill_model_test;Username=test;Password=test")
            .Options;
        using var dbContext = new TaskExecutionDbContext(options);

        var entity = dbContext.Model.FindEntityType(typeof(TaskSkill))!;

        entity.GetTableName().Should().Be("task_skill");
        entity.FindProperty(nameof(TaskSkill.RequiredSkillLevel))!.GetMaxLength().Should().Be(30);
        entity.GetIndexes().Should().ContainSingle(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(TaskSkill.TaskId), nameof(TaskSkill.SkillId) }));
        entity.GetForeignKeys().Should().ContainSingle(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(WorkTask));
    }
}

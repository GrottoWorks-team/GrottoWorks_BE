using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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

        entity.GetTableName().Should().Be("task");
        entity.FindProperty(nameof(WorkTask.Name))!.GetMaxLength().Should().Be(200);
        entity.FindProperty(nameof(WorkTask.EstimatedHours))!.GetPrecision().Should().Be(6);
        entity.FindProperty(nameof(WorkTask.Version))!.IsConcurrencyToken.Should().BeTrue();
        entity.GetForeignKeys().Should().BeEmpty("TaskExecution stores external ownership as IDs only");
    }
}

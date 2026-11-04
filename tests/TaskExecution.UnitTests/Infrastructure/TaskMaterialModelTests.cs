using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskExecution.Domain.Tasks;
using TaskExecution.Infrastructure.Data;
using Xunit;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class TaskMaterialModelTests
{
    [Fact]
    public void Materials_use_local_task_fk_precision_and_unique_reference_per_task()
    {
        using var db = new TaskExecutionDbContext(new DbContextOptionsBuilder<TaskExecutionDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only").Options);
        var entity = db.Model.FindEntityType(typeof(TaskMaterial))!;
        entity.GetTableName().Should().Be("task_material");
        entity.FindProperty(nameof(TaskMaterial.RequiredQuantity))!.GetPrecision().Should().Be(12);
        entity.FindProperty(nameof(TaskMaterial.RequiredQuantity))!.GetScale().Should().Be(2);
        entity.GetIndexes().Should().Contain(index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { nameof(TaskMaterial.TaskId), nameof(TaskMaterial.MaterialId) }));
        entity.GetForeignKeys().Should().ContainSingle().Which.PrincipalEntityType.ClrType.Should().Be(typeof(WorkTask));
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskExecution.Domain.Assignments;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class AssignmentModelTests
{
    [Fact]
    public void Active_volunteer_is_unique_and_assignment_has_optimistic_concurrency()
    {
        using var db = new TaskExecutionDbContext(new DbContextOptionsBuilder<TaskExecutionDbContext>()
            .UseNpgsql("Host=localhost;Database=not_used;Username=test;Password=test").Options);
        var entity = db.Model.FindEntityType(typeof(TaskAssignment))!;
        entity.GetTableName().Should().Be("task_assignment");
        entity.FindProperty(nameof(TaskAssignment.Version))!.IsConcurrencyToken.Should().BeTrue();
        entity.GetIndexes().Should().Contain(index=>index.IsUnique && index.GetFilter()!.Contains("PENDING"));
        entity.GetForeignKeys().Should().ContainSingle(fk=>fk.PrincipalEntityType.GetTableName()=="task");
        db.Database.HasPendingModelChanges().Should().BeFalse("the migration snapshot must match the runtime model");
    }
}

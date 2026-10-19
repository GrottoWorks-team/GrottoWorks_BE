using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskExecution.Domain.Scopes;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class ScopeProjectionModelTests
{
    [Fact]
    public void Scope_entities_follow_projection_schema()
    {
        var options = new DbContextOptionsBuilder<TaskExecutionDbContext>()
            .UseNpgsql("Host=localhost;Database=task_model_test;Username=test;Password=test")
            .Options;
        using var dbContext = new TaskExecutionDbContext(options);

        var season = dbContext.Model.FindEntityType(typeof(SeasonScope))!;
        var workArea = dbContext.Model.FindEntityType(typeof(WorkAreaScope))!;

        season.GetTableName().Should().Be("season_scope");
        season.FindProperty(nameof(SeasonScope.Status))!.GetMaxLength().Should().Be(30);
        workArea.GetTableName().Should().Be("work_area_scope");
        workArea.FindProperty(nameof(WorkAreaScope.Name))!.GetMaxLength().Should().Be(150);
        workArea.GetIndexes().Select(index => index.GetDatabaseName()).Should().Contain(
            "ix_work_area_scope_season_id",
            "ix_work_area_scope_parish_id",
            "ix_work_area_scope_leader_user_id");
        workArea.GetForeignKeys().Should().BeEmpty("external projections must not depend on snapshot arrival order");
    }
}

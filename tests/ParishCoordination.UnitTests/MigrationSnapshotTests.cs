using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ParishCoordination.Infrastructure.Data;
using ParishCoordination.Infrastructure.Data.Migrations;
using Xunit;

namespace ParishCoordination.UnitTests;

public sealed class MigrationSnapshotTests
{
    [Fact]
    public void Http_outbox_drop_requires_published_current_versions_from_actual_parish_tables()
    {
        var operations = new RemoveTaskScopeHttpOutbox().UpOperations;
        var guard = Assert.IsType<SqlOperation>(operations[0]).Sql;
        Assert.Contains("FROM christmas_season s", guard);
        Assert.DoesNotContain("FROM season s", guard);
        Assert.Contains("FROM work_area a", guard);
        Assert.Contains("p.source_version = s.version", guard);
        Assert.Contains("p.source_version = a.version", guard);
        Assert.Contains("p.published_at IS NOT NULL", guard);
        Assert.Contains("p.envelope->>'scopeType' = 'season'", guard);
        Assert.Contains("p.envelope->>'scopeType' = 'work-area'", guard);
        Assert.Contains("pg_advisory_xact_lock(71110305)", guard);
        Assert.Contains("pg_advisory_xact_lock(71110304)", guard);
        Assert.Contains("RAISE EXCEPTION", guard);
        Assert.Equal("task_scope_outbox", Assert.IsType<DropTableOperation>(operations[1]).Name);
        Assert.Equal(2, operations.Count);
    }

    [Fact]
    public void Historical_outbox_model_is_frozen_while_current_and_forward_models_exclude_it()
    {
        const string entityName = "ParishCoordination.Infrastructure.TaskScopeSync.TaskScopeOutbox";
        var historical = new AddTaskScopeOutbox().TargetModel.FindEntityType(entityName);
        Assert.NotNull(historical);
        Assert.Equal("task_scope_outbox", historical.GetTableName());
        Assert.Null(new RemoveTaskScopeHttpOutbox().TargetModel.FindEntityType(entityName));
        using var db = new ParishCoordinationDbContext(new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        Assert.Null(db.Model.FindEntityType(entityName));
    }

    [Fact]
    public void Current_model_matches_the_frozen_migration_snapshot()
    {
        using var db = new ParishCoordinationDbContext(new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}

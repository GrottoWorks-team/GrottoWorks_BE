using Microsoft.EntityFrameworkCore;
using Readiness.Infrastructure.Data;
using Xunit;

namespace Readiness.UnitTests;

public sealed class MigrationSnapshotTests
{
    [Fact]
    public void Current_model_matches_the_frozen_migration_snapshot()
    {
        using var db = new ReadinessDbContext(new DbContextOptionsBuilder<ReadinessDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}

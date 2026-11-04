using Microsoft.EntityFrameworkCore;
using Resource.Infrastructure.Data;
using Xunit;

namespace Resource.UnitTests;

public sealed class MigrationSnapshotTests
{
    [Fact]
    public void Current_model_matches_the_frozen_migration_snapshot()
    {
        using var db = new ResourceDbContext(new DbContextOptionsBuilder<ResourceDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}

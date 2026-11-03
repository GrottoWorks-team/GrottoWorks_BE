using Microsoft.EntityFrameworkCore;
using ParishCoordination.Infrastructure.Data;
using Xunit;

namespace ParishCoordination.UnitTests;

public sealed class MigrationSnapshotTests
{
    [Fact]
    public void Current_model_matches_the_frozen_migration_snapshot()
    {
        using var db = new ParishCoordinationDbContext(new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}

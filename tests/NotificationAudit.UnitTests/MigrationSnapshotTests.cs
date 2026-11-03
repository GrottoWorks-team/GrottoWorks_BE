using Microsoft.EntityFrameworkCore;
using NotificationAudit.Infrastructure.Data;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class MigrationSnapshotTests
{
    [Fact]
    public void Current_model_matches_the_frozen_migration_snapshot()
    {
        using var db = new NotificationAuditDbContext(new DbContextOptionsBuilder<NotificationAuditDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}

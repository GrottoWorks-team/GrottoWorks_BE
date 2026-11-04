using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;

internal static class OutboxRollback
{
    public static async Task VerifyAsync(ParishCoordinationDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var parish = Parish.Create(Guid.NewGuid(), "Rollback verification", null, null, now);
        var season = Season.Create(parish.Id, Guid.Empty, "Rollback verification", 2026,
            new(2026, 1, 1), new(2026, 12, 31), null, null, now);
        // A nonexistent internal FK causes the real EF/PostgreSQL save to fail.
        var area = WorkArea.Create(season.Id, Guid.NewGuid(), null, null, null,
            "Rollback verification", null, null, null, WorkAreaStatus.Planned, now);
        db.AddRange(parish, season, area);
        try
        {
            await db.SaveChangesAsync();
            throw new InvalidOperationException("Expected FK failure was not raised.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23503" })
        {
            db.ChangeTracker.Clear();
        }
        if (await db.Parishes.AnyAsync(x => x.Id == parish.Id) ||
            await db.Seasons.AnyAsync(x => x.Id == season.Id) ||
            await db.WorkAreas.AnyAsync(x => x.Id == area.Id) ||
            await db.PlanningEventOutbox.AnyAsync(x => x.AggregateId == season.Id || x.AggregateId == area.Id))
            throw new InvalidOperationException("Business/outbox rows survived a failed PostgreSQL transaction.");
        Console.WriteLine("PostgreSQL FK failure rolled back Parish, Season, Work Area and planning outbox atomically.");
    }
}
